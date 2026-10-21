# TIA Portal MCP Server — Guía operativa V17

> **Estado:** repositorio adaptado a **TIA Portal V17**.
> **Verificado en vivo:** 2026-10-21 contra un proyecto V17 (`PortalVersion: V17`), con build Release sin errores y **47/47 tests** pasando.
> **Alcance:** instalación, conexión a una sesión abierta de TIA Portal, herramientas MCP, generación de LAD, ejemplos de motor, SimaticML V17, diagnóstico y publicación en GitHub.

Esta guía reemplaza la nota histórica de V20. Los archivos de `docs/lad-samples/` siguen siendo fixtures reales de V20 y **no deben modificarse**; los tests normalizan sus namespaces a los esquemas que genera esta versión.

---

## Índice

1. [Qué es este proyecto](#1-qué-es-este-proyecto)
2. [Estado y diferencias de la versión V17](#2-estado-y-diferencias-de-la-versión-v17)
3. [Requisitos](#3-requisitos)
4. [Instalación y compilación](#4-instalación-y-compilación)
5. [Primer arranque y conexión](#5-primer-arranque-y-conexión)
6. [Modos de ejecución y configuración MCP](#6-modos-de-ejecución-y-configuración-mcp)
7. [Herramientas MCP disponibles](#7-herramientas-mcp-disponibles)
8. [Flujo de trabajo recomendado](#8-flujo-de-trabajo-recomendado)
9. [Generación de bloques LAD](#9-generación-de-bloques-lad)
10. [Ejemplo completo: control y supervisión de motor](#10-ejemplo-completo-control-y-supervisión-de-motor)
11. [SimaticML y reglas de V17](#11-simaticml-y-reglas-de-v17)
12. [API HTTP y dashboard](#12-api-http-y-dashboard)
13. [Diagnóstico y errores frecuentes](#13-diagnóstico-y-errores-frecuentes)
14. [Tests y validación](#14-tests-y-validación)
15. [Limitaciones conocidas](#15-limitaciones-conocidas)
16. [Publicar el proyecto en GitHub](#16-publicar-el-proyecto-en-github)
17. [Mapa del proyecto](#17-mapa-del-proyecto)
18. [Checklist rápido](#18-checklist-rápido)

---

## 1. Qué es este proyecto

`TIA Portal MCP Server` es una aplicación Windows que se conecta a una instancia **ya abierta** de TIA Portal mediante **TIA Portal Openness** y expone sus operaciones por dos vías:

- **MCP (Model Context Protocol)**, para que un agente de IA pueda listar dispositivos, leer y escribir bloques, crear tags, compilar y guardar el proyecto.
- **HTTP/REST + dashboard**, para operar manualmente desde una ventana nativa con WebView2.

La aplicación **no reemplaza TIA Portal**: TIA Portal debe estar abierto con el proyecto cargado. El servidor se adjunta a esa sesión, ejecuta las operaciones y no cierra el proyecto al terminar.

### Arquitectura

```text
Cliente MCP (Claude Code, Cursor, VS Code, OpenCode, etc.)
        │  stdio: JSON-RPC delimitado por líneas
        ▼
TiaPortalDashboard.exe --mcp-stdio
        │
        │  TIA Openness API (COM, requiere STA)
        ▼
Siemens.Engineering.dll  →  TIA Portal V17 abierto con un proyecto
```

En modo HTTP:

```text
Navegador / Claude Desktop
        │  HTTP :5000
        ▼
TiaPortalDashboard.exe  →  dashboard + /mcp + /api/*
        │
        ▼
TIA Portal V17
```

Todas las llamadas a Openness se ejecutan en un único hilo **STA** (`StaTaskScheduler`). No se deben llamar desde hilos del pool.

---

## 2. Estado y diferencias de la versión V17

| Elemento | Estado |
|---|---|
| Framework | `.NET Framework 4.8` (`net48`) |
| TIA Portal | V17 |
| Proyecto | `.ap17` |
| Referencias Openness | `C:\Program Files\Siemens\Automation\Portal V17\PublicAPI\V17` |
| Herramientas MCP | **32** en el build V17 por defecto |
| HMI WinCC Unified | **No disponible** en V17; 7 herramientas excluidas |
| Tests | **47/47** |
| Build Release | **0 errores, 0 warnings** (verificado 2026-10-21) |
| Proyecto al desconectar | **No se cierra** si el servidor solo se adjuntó |

### Cambios importantes respecto de V20

1. **No existe `Siemens.Engineering.HmiUnified` en V17.** `HmiTagService.cs` y `HmiScreenService.cs` se excluyen del build por defecto. Para V18+ se puede compilar con `-p:TiaHmiUnified=true`.
2. **V17 no expone el atributo `PortalVersion`.** El servidor lo deriva de la extensión del proyecto (`.ap17` → `V17`).
3. **Un OB V17 no acepta `<Namespace />`.** El builder lo omite en OB y lo mantiene en FB/FC. Si se incluye, TIA devuelve: `The 'Namespace' attribute at the 'SW.Blocks.OB' object ... is not supported.`
4. **Un OB V17 no acepta `Title` ni `Comment` como atributos** vía Openness. Las tags sí aceptan comentarios.
5. **Import y export de esquemas no son simétricos.** El builder genera `Interface/v4` y `FlgNet/v4`; TIA V17 **acepta** esos esquemas al importar, pero al **exportar** puede devolver `Interface/v5`. No hay que cambiar el builder a v5 solo porque un export muestre v5.
6. **`Dispose()` no cierra el proyecto del usuario.** Los clientes MCP stdio son de corta vida; cerrar el proyecto al salir borraba el trabajo abierto en TIA. El cierre explícito queda disponible con `close_project`.
7. **No se agregó ningún HMI ni se modificó OB1** en los ejemplos de motor de esta guía.

---

## 3. Requisitos

- Windows 10 u 11.
- **TIA Portal V17** instalado y licenciado.
- Cuenta de Windows perteneciente al grupo **`Siemens TIA Openness`**.
- **.NET SDK 8 o superior** para compilar.
- **.NET Framework 4.8** (incluido en Windows 10/11).
- **Microsoft Edge / WebView2** para el dashboard.
- TIA Portal abierto con el proyecto cargado para usar Openness.

### Agregar el usuario al grupo de Openness

1. Abrir **Administración de equipos**.
2. Ir a **Usuarios y grupos locales → Grupos**.
3. Abrir **`Siemens TIA Openness`**.
4. Agregar la cuenta de Windows.
5. Cerrar sesión y volver a entrar.

Sin este paso, la conexión falla con un error de seguridad.

---

## 4. Instalación y compilación

### Clonar

```powershell
git clone https://github.com/Macsion/tia-portal-mcp-v17.git
cd tia-portal-mcp-master
```

### Compilar

```powershell
$env:PATH = "C:\Program Files\dotnet;$env:PATH"
dotnet build .\src\TiaOpennessMcpServer\TiaOpennessMcpServer.csproj -c Release
```

Salida:

```text
src\TiaOpennessMcpServer\bin\Release\net48\TiaPortalDashboard.exe
```

### Tests

```powershell
dotnet test .\tests\LadXmlBuilder.Tests\LadXmlBuilder.Tests.csproj -c Release
```

Resultado esperado en esta versión: **47 tests, 0 fallos**.

### Si TIA Portal está instalado en otra ruta

Editar `src/TiaOpennessMcpServer/TiaOpennessMcpServer.csproj` y cambiar los `HintPath` de `Siemens.Engineering.dll` y `Siemens.Engineering.Hmi.dll`.

### Build con HMI Unified (solo V18+)

```powershell
dotnet build .\src\TiaOpennessMcpServer\TiaOpennessMcpServer.csproj -c Release -p:TiaHmiUnified=true
```

En V17 esa compilación no es posible porque el namespace `Siemens.Engineering.HmiUnified` no existe.

---

## 5. Primer arranque y conexión

1. Abrir **TIA Portal V17**.
2. Abrir el proyecto `.ap17`.
3. Ejecutar `TiaPortalDashboard.exe`:
   - Sin argumentos: dashboard + servidor HTTP en `http://localhost:5000`.
   - Con `--mcp-stdio`: modo MCP por stdin/stdout, sin ventana.
4. Llamar `connect_to_tia_portal`.
5. TIA Portal muestra el diálogo **Openness access**; hacer clic en **“Sí a todo”**.
6. Verificar con `get_status` que devuelve `connected: true` y `portalVersion: "V17"`.

### Diálogo de aprobación

- Aparece por cada **proceso nuevo** que se conecta, no una sola vez para siempre.
- Puede quedar detrás de otras ventanas: revisar el botón de TIA Portal en la barra de tareas.
- La llamada a `connect_to_tia_portal` queda bloqueada hasta que se aprueba. Configurar timeouts de cliente de **90 segundos o más**.
- Mientras el hilo STA está bloqueado, las demás operaciones Openness quedan en cola.

### El proyecto no se cierra al salir

Al terminar el proceso MCP, el servidor **no cierra** el proyecto al que se adjuntó. Esto es intencional y fue verificado: el proyecto siguió abierto después de cerrar el cliente MCP.

Para cerrar el proyecto explícitamente:

```json
{ "name": "close_project", "arguments": { "confirm": true } }
```

> `close_project` descarta cambios no guardados si `AutoSaveOnDisconnect` es `false`. Guardar antes con `save_project`.

---

## 6. Modos de ejecución y configuración MCP

### 6.1 stdio (recomendado para agentes)

```powershell
TiaPortalDashboard.exe --mcp-stdio
```

El proceso no abre ventana ni servidor HTTP. El cliente MCP lo inicia y se comunica por stdin/stdout.

Ejemplo de configuración genérica:

```json
{
  "mcpServers": {
    "tia-portal": {
      "command": "C:\\ruta\\al\\repo\\src\\TiaOpennessMcpServer\\bin\\Release\\net48\\TiaPortalDashboard.exe",
      "args": ["--mcp-stdio"]
    }
  }
}
```

- **Claude Code:** usar el objeto `mcpServers` de `~/.claude.json` o un `.mcp.json` en la raíz del proyecto. No usar `~/.claude/.mcp.json` ni `~/.claude/settings.json`.
- **Cursor / VS Code Copilot:** usar la configuración MCP del cliente con el mismo `command` y `args`.
- **OpenCode:** usar la configuración de MCP del cliente; el servidor es un stdio MCP estándar.
- **Claude Desktop:** para HTTP, agregar un **Custom Connector** en `Settings → Connectors` apuntando a `http://localhost:5000/mcp`. No agregar HTTP en `claude_desktop_config.json`.

### 6.2 HTTP / dashboard

```powershell
TiaPortalDashboard.exe
```

- Dashboard: `http://localhost:5000/`
- MCP: `http://localhost:5000/mcp`
- REST: `http://localhost:5000/api/...`
- `GET /mcp` devuelve `405` con cuerpo JSON-RPC para clientes que detectan Streamable HTTP.
- Protocolo MCP: `2025-03-26`; acepta `2024-11-05` si el cliente lo solicita.

### 6.3 Argumentos de línea de comandos

| Argumento | Efecto |
|---|---|
| `--mcp-stdio` | Modo MCP stdio; no inicia dashboard ni HTTP. |
| `--project <ruta.ap17>` | Abre el proyecto al iniciar; si falla, el servidor igual arranca y escribe el error en stderr. |
| `--with-ui` | Con `--project`, abre TIA Portal visible en lugar de headless. |
| `--profile readonly\|lite\|standard\|full` | Limita las herramientas publicadas y ejecutables. |
| `--annotations` | Agrega `readOnlyHint` / `destructiveHint` a `tools/list`. |
| `TIA_MCP_PROFILE` | Variable de entorno equivalente a `--profile`. |
| `TIA_MCP_ANNOTATIONS=1` | Equivalente a `--annotations`. |

### 6.4 Perfiles (build V17)

| Perfil | Herramientas V17 | Contenido |
|---|---:|---|
| `readonly` | 15 | Solo lectura y diagnóstico. Incluye `compile_block`, que no cambia contenido. |
| `lite` | 10 | Conectar, navegar dispositivos/bloques/tags, leer/escribir SCL, compilar y guardar. |
| `standard` | 27 | Todo lo de `lite` más creación/edición de bloques, tags y firmas. |
| `full` | 32 | Todo, incluidos `open_project`, `close_project`, `clone_project`, `get_option_packages` y `generate_s7_1200`. |

Las herramientas fuera del perfil se ocultan en `tools/list` y además se rechazan en el dispatch.

### 6.5 Configuración de opciones

`appsettings.json` **no se carga**. `Program.cs` llama a:

```csharp
services.Configure<TiaOpennessOptions>(_ => { });
```

Por lo tanto, los valores efectivos son los defaults de `src/TiaOpennessMcpServer/Services/TiaOpennessOptions.cs`, no los del JSON. Para cambiar, por ejemplo, el directorio de exportación, editar esa clase y recompilar.

| Opción | Default |
|---|---|
| `Version` | `V17` |
| `DefaultMode` | `WithoutUserInterface` |
| `ConnectionTimeoutSeconds` | `60` |
| `AutoSaveOnDisconnect` | `true` |
| `ExportDirectory` | `C:\Temp\TiaExports` |

---

## 7. Herramientas MCP disponibles

El build V17 publica **32 herramientas**. Las 7 herramientas HMI de WinCC Unified se compilan solo con `TiaHmiUnified=true` (V18+).

### 7.1 Conexión y proyecto

| Herramienta | Argumentos | Devuelve / uso |
|---|---|---|
| `connect_to_tia_portal` | opcional `projectPath` | Se adjunta a TIA V17 abierto. En el dispatch actual `projectPath` se ignora; si hay varias instancias, cerrar las que no se usan. |
| `get_status` | — | `{connected:false}` o `{connected:true, project:{...}}`; no bloquea. |
| `save_project` | — | Guarda el proyecto abierto. |
| `get_project_signature` | — | Índice de dispositivos, bloques, tags y estado de consistencia. |
| `get_option_packages` | — | Paquetes de opción y productos usados. |
| `open_project` | `path`; opcional `headless` | Abre un `.ap17`; inicia TIA si no hay instancia. |
| `close_project` | `confirm: true` | Cierra el proyecto. Puede perder cambios no guardados. |
| `clone_project` | `name`, `path` | Clona el proyecto abierto a otra carpeta. |

### 7.2 Dispositivos y hardware

| Herramienta | Argumentos | Devuelve / uso |
|---|---|---|
| `list_devices` | — | PLCs, HMIs y drives con módulos. |
| `get_device` | `device` | Tipo, orden, firmware y dirección. |
| `get_io_mapping` | `device` | Puntos de I/O: módulo, canal, dirección y dirección. Usar antes de crear tags. |
| `generate_s7_1200` | `confirm`, `deviceName`, `cpuVariant`, `ipAddress`; opcionales `subnetMask`, `gateway`, `signalModules`, `signalBoards`, `commsModules`, `enableProfinet` | Crea una estación S7-1200. Requiere confirmación explícita. |

### 7.3 Bloques

| Herramienta | Argumentos | Devuelve / uso |
|---|---|---|
| `list_blocks` | `device` | Bloques, incluidas subcarpetas. |
| `read_block` | `device`, `block` | `sourceCode` (SCL) y `xmlContent`. |
| `write_block_scl` | `device`, `block`, `source` | Sobrescribe SCL; compilar después. |
| `import_block_xml` | `device`, `block`, `content` | Importa SimaticML crudo (LAD/FBD/STL/SCL). |
| `compile_block` | `device`, `block` | Texto de compilación con errores/advertencias. |
| `analyze_block` | `device`, `block` | Análisis estático SCL. |
| `create_block` | `device`, `name`, `type`, `sourceCode`; opcional `number` | Crea FB/FC/OB/GlobalDB **siempre en SCL**. |
| `create_lad_block` | `device`, `name`, `type`, `networks`; opcionales `number`, `interface`, `culture`, `overwrite`, `compile`, `dryRun` | Crea LAD desde una descripción estructurada, importa y compila. |
| `create_instance_db` | `device`, `name`, `instanceOfName`; opcional `number` | DB de instancia para un FB. |
| `get_block_attributes` | `device`, `block` | Atributos y composiciones legibles/escribibles. |
| `patch_block_texts` | `device`, `block`, `texts` | Título/comentario de bloque y redes. Ver limitaciones V17. |
| `export_block` | `device`, `block`; opcional `path` | Exporta SimaticML a disco. |
| `export_tag_table` | `device`, `table`; opcional `path` | Exporta tabla de tags a disco. |

### 7.4 Tags

| Herramienta | Argumentos | Devuelve / uso |
|---|---|---|
| `list_tag_tables` | `device` | Tablas y cantidad de tags. |
| `get_tags` | `device`, `table` | Tags con tipo, dirección y comentario. |
| `create_tag_table` | `device`, `table` | Crea una tabla vacía. |
| `create_tag` | `device`, `table`, `name`, `dataType`, `address`; opcionales `accessible`, `writable`, `comment` | Crea una tag individual. |
| `import_tag_table` | `device`, `content` | **Reemplaza** la tabla completa. Leer y fusionar antes. |
| `batch_rename_tags` | `device`, `table`, `renames` | Renombra en una operación atómica. |

### 7.5 Análisis

| Herramienta | Argumentos | Devuelve / uso |
|---|---|---|
| `analyze_scl` | `source`; opcionales `blockName`, `blockType` | Diagnóstico SCL sin bloque abierto. |

### 7.6 HMI (solo build V18+)

Con `TiaHmiUnified=true` se agregan:

`list_hmi_tag_tables`, `get_hmi_tags`, `get_all_hmi_tags`, `create_hmi_tags`, `list_hmi_screens`, `get_screen_tag_refs`, `update_faceplate_tags`.

En el build V17 por defecto **no existen** y no aparecen en `tools/list`.

### 7.7 Quirks importantes

- **`connect_to_tia_portal.projectPath` está declarado pero el dispatch actual no lo pasa** a `AttachToRunningAsync`. Para elegir instancia, cerrar las demás.
- **`create_block` siempre crea SCL.** El campo `type` elige FB/FC/OB/GlobalDB, no el lenguaje.
- **`create_lad_block` es el camino para LAD.** No escribir XML a mano salvo que se necesite una función no soportada.
- **`import_tag_table` reemplaza, no fusiona.** Si se omite una tag, se pierde.
- **`patch_block_texts`:** en V17 un **OB** rechaza `Title` y `Comment` (`'Title' is not supported by type 'Siemens.Engineering.SW.Blocks.OB'`). Además, el servicio actual solo aplica `blockTitle`/`blockComment`; el array `networks` se acepta en el esquema pero no se procesa.
- **Errores MCP:** vuelven como `isError: true` con la primera línea de la excepción. El log de las últimas llamadas está en `GET /api/mcp/log` y en el dashboard.
- **Modo online:** Openness rechaza crear bloques si el PLC está online. Trabajar offline.

---

## 8. Flujo de trabajo recomendado

### 8.1 Conectar y fotografiar el estado

```text
1. Abrir TIA Portal V17 con el proyecto.
2. connect_to_tia_portal            → aprobar “Sí a todo”.
3. get_status                       → confirmar connected/V17.
4. list_devices                     → anotar el nombre EXACTO del device.
5. get_project_signature            → foto antes de cambiar.
```

> El nombre para Openness es el **nombre del device** (por ejemplo `S7-1200 station_1`), no el nombre de la CPU (`PLC_1`). `GetPlcSoftware` busca por device.

### 8.2 Crear tags

Opción A, una por una:

```json
{
  "name": "create_tag",
  "arguments": {
    "device": "S7-1200 station_1",
    "table": "Default tag table",
    "name": "Motor",
    "dataType": "Bool",
    "address": "%Q0.0",
    "accessible": true,
    "writable": true,
    "comment": "Contactor del motor. TRUE = motor en marcha."
  }
}
```

Opción B, tabla completa con `import_tag_table` (más rápida, pero **reemplaza** la tabla).

### 8.3 Crear un bloque LAD

```text
1. create_lad_block con dryRun:true   → valida y muestra el XML, sin tocar TIA.
2. create_lad_block con dryRun:false  → importa y compila.
3. Revisar compileOutput: Errors: 0 / Warnings: 0.
4. list_blocks / get_project_signature → confirmar isConsistent:true.
5. save_project.
```

### 8.4 Modificar un bloque SCL

```text
read_block → editar source → write_block_scl → compile_block → save_project
```

### 8.5 Guardar y verificar

- `save_project` guarda en disco.
- `get_status.project.isModified` debe quedar en `false`.
- `get_project_signature` debe mostrar `isConsistent: true` en los bloques tocados.
- Al salir del cliente MCP, el proyecto debe seguir abierto en TIA.

---

## 9. Generación de bloques LAD

### 9.1 Modelo de entrada

`create_lad_block` recibe:

```jsonc
{
  "name": "MiBloque",
  "type": "OB",                 // FB, FC u OB
  "number": 124,                // opcional; recomendable para OB
  "interface": {
    "input":    [{ "name": "Start", "datatype": "Bool" }],
    "output":   [{ "name": "Motor", "datatype": "Bool" }],
    "inOut":    [],
    "static":   [],
    "temp":     [],
    "constant": []
  },
  "networks": [
    {
      "title": "Marcha / parada",
      "comment": "Enclavamiento",
      "elements": [
        {
          "type": "branch",
          "branches": [
            [{ "type": "contact", "operand": "Start" }],
            [{ "type": "contact", "operand": "Motor" }]
          ]
        },
        { "type": "contact", "operand": "Stop_OK" }
      ],
      "outputs": [{ "type": "coil", "operand": "Motor" }]
    }
  ],
  "culture": "es-ES",           // solo si hay title/comment
  "compile": true,
  "dryRun": false
}
```

### 9.2 Elementos soportados

- `contact` (con `negated: true` para NC).
- `coil`, `scoil` (set), `rcoil` (reset).
- `branch` con dos o más caminos paralelos.
- Comparaciones: `eq`, `ne`, `gt`, `ge`, `lt`, `le`.
- Detección de flanco: `pbox`, `nbox`.
- Temporizadores: `ton`, `tof`, `tp`.
- Contadores: `ctu`, `ctd`.
- `sr`, `rs`.
- Aritmética: `add`, `sub`, `mul`, `div`, `mod`.
- `norm_x`, `scale_x`.
- `move` (último elemento de la red).
- `call` de FB (con DB de instancia o `#multi`) o FC con parámetros nombrados.
- `part` para funciones de sistema.

Los operandos pueden ser:

- Tag global: `Motor`, `Start_PB`.
- Miembro de DB: `"MiDB".Miembro`.
- Variable local: `#Motor`.

### 9.3 Reglas de interfaz por tipo

| Tipo | Secciones permitidas |
|---|---|
| FB | Input, Output, InOut, Static, Temp, Constant |
| FC | Input, Output, InOut, Temp, Constant + Return |
| OB | **solo** Input, Temp, Constant |

Un OB **no** puede tener Output, InOut ni Static. Por eso el control de motor de ejemplo usa tags globales y no variables de salida del OB.

### 9.4 Numeración de OB

- Si no se indica `number`, el servidor busca el primer número libre desde **123**.
- En `dryRun` sin número, muestra 123 aunque esté ocupado, porque no consulta TIA.
- Para resultados reproducibles, pasar `number` explícito (por ejemplo 123, 124).
- El OB se crea con `SecondaryType=ProgramCycle`, por lo que se ejecuta cíclicamente sin llamarlo desde OB1.

### 9.5 `dryRun`

`dryRun:true`:

- Construye el XML.
- Ejecuta el validador interno (UIds, wires, cardinalidades, ramas).
- **No importa ni compila**.
- Devuelve el XML para inspección.

Es la primera comprobación recomendada antes de tocar el proyecto.

### 9.6 Ejemplo mínimo de enclavamiento

```json
{
  "name": "Motor_StartStop",
  "type": "OB",
  "number": 123,
  "interface": {
    "input": [], "output": [], "inOut": [], "static": [], "temp": [], "constant": []
  },
  "networks": [
    {
      "elements": [
        {
          "type": "branch",
          "branches": [
            [{ "type": "contact", "operand": "Start_PB" }],
            [{ "type": "contact", "operand": "Motor" }]
          ]
        },
        { "type": "contact", "operand": "Stop_OK" },
        { "type": "contact", "operand": "Thermal_OK" }
      ],
      "outputs": [{ "type": "coil", "operand": "Motor" }]
    }
  ],
  "compile": true,
  "dryRun": false
}
```

Lógica: `Motor = (Start_PB OR Motor) AND Stop_OK AND Thermal_OK`.

---

## 10. Ejemplo completo: control y supervisión de motor

> **Seguridad:** este ejemplo puede accionar un contactor real. No descargar ni forzar salidas en una máquina sin analizar riesgos, enclavamientos y parada de emergencia. Para pruebas, usar PLCSIM o un banco seguro.

### 10.1 Tags del ejemplo

Se usaron direcciones de una CPU S7-1200 con DI/DQ onboard. Verificar siempre con `get_io_mapping` antes de copiar direcciones.

| Tag | Dirección | Tipo | Comentario |
|---|---|---|---|
| `Start_PB` | `%I0.0` | Bool | Pulsador de marcha (NA). `TRUE` = accionado. |
| `Stop_OK` | `%I0.1` | Bool | Contacto **NC** de paro. `TRUE` = circuito sano. |
| `Thermal_OK` | `%I0.2` | Bool | Contacto **NC** del relé térmico. `TRUE` = sin falla. |
| `Motor` | `%Q0.0` | Bool | Contactor del motor. `TRUE` = marcha. |
| `Thermal_Fault` | `%Q0.1` | Bool | Indicación de falla térmica. |
| `Motor_Run_Status` | `%M0.0` | Bool | Motor en marcha y térmico sano. |
| `Motor_Stop_Status` | `%M0.1` | Bool | Motor detenido y térmico sano. |
| `Motor_Thermal_Trip` | `%M0.2` | Bool | Térmico disparado. |
| `Motor_State_Fault` | `%M0.3` | Bool | Inconsistencia: motor activo con térmico disparado o paro abierto. |

**Convención:** `Stop_OK` y `Thermal_OK` representan contactos **NC** cableados a la entrada. En reposo valen `TRUE`. Si se prueba con pulsadores NA de banco, hay que invertir esas dos señales.

### 10.2 OB123 — `Motor_StartStop`

Red 1:

```text
Motor = (Start_PB OR Motor) AND Stop_OK AND Thermal_OK
```

- `Start_PB` y `Motor` en paralelo: enclavamiento.
- `Stop_OK` y `Thermal_OK` en serie.
- Si se abre el paro o el térmico, el motor cae en el mismo ciclo.

Red 2:

```text
Thermal_Fault = NOT Thermal_OK
```

Resultado verificado:

- Importación: OK.
- Compilación: **0 errores, 0 warnings**.
- `isConsistent: true`.
- Proyecto guardado.

### 10.3 OB124 — `Motor_Status`

| Red | Lógica | Salida |
|---|---|---|
| 1 | `Motor AND Thermal_OK` | `Motor_Run_Status` |
| 2 | `NOT Motor AND Thermal_OK` | `Motor_Stop_Status` |
| 3 | `NOT Thermal_OK` | `Motor_Thermal_Trip` |
| 4 | `Motor AND (NOT Thermal_OK OR NOT Stop_OK)` | `Motor_State_Fault` |

Tabla de estados:

| Escenario | Run | Stop | Trip | State_Fault |
|---|---:|---:|---:|---:|
| Detenido, térmico sano | 0 | 1 | 0 | 0 |
| En marcha, térmico sano | 1 | 0 | 0 | 0 |
| Térmico disparado | 0 | 0 | 1 | 0 |
| Motor activo con térmico disparado o paro abierto | 1 | 0 | 1 | 1 |

`Motor_State_Fault` es un diagnóstico de inconsistencia. En operación normal, cuando el térmico dispara, OB123 ya bajó `Motor`, por lo que `State_Fault` queda en 0 y `Thermal_Trip` en 1.

Resultado verificado:

- Importación: OK.
- Compilación: **0 errores, 0 warnings**.
- `isConsistent: true`.
- Proyecto guardado.

### 10.4 Cómo probarlo en TIA

1. Abrir **Program blocks → Motor_StartStop (OB123)** y **Motor_Status (OB124)**.
2. Activar monitorización.
3. Forzar `Stop_OK = TRUE`, `Thermal_OK = TRUE`.
4. Dar un pulso a `Start_PB` → `Motor = TRUE`, `Motor_Run_Status = TRUE`.
5. Forzar `Stop_OK = FALSE` → `Motor = FALSE`, `Motor_Stop_Status = TRUE`.
6. Forzar `Thermal_OK = FALSE` → `Motor = FALSE`, `Motor_Thermal_Trip = TRUE`.
7. Restaurar `Thermal_OK = TRUE` → la falla se apaga, el motor **no rearranca solo**: hay que dar marcha otra vez.
8. Para ver `Motor_State_Fault`, forzar `Motor = TRUE` con `Thermal_OK = FALSE` o `Stop_OK = FALSE`.

> Los bloques del ejemplo **no se descargaron al PLC**. Están compilados y guardados en el proyecto. La descarga requiere conexión al PLC y una decisión explícita del usuario.

---

## 11. SimaticML y reglas de V17

### 11.1 Esquemas

El builder genera:

```xml
<Engineering version="V17" />
<Section xmlns="http://www.siemens.com/automation/Openness/SW/Interface/v4">
<FlgNet xmlns="http://www.siemens.com/automation/Openness/SW/NetworkSource/FlgNet/v4">
```

**Hecho verificado:** TIA V17 acepta `Interface/v4` y `FlgNet/v4` al importar. Al exportar, V17 puede devolver `Interface/v5`. Eso no significa que el import deba cambiarse a v5.

Los fixtures de `docs/lad-samples/` son exportaciones reales de V20 con v5; `GoldenSamples.cs` normaliza namespaces para comparar contra el builder V17. **No reescribir los fixtures históricos.**

### 11.2 OB: sin `<Namespace />`

Un OB V17 debe omitir `<Namespace />` dentro de `<AttributeList>`. Si se incluye:

```text
The 'Namespace' attribute at the 'SW.Blocks.OB' object ... is not supported.
```

El builder hace:

```csharp
// V17 rejects <Namespace /> on SW.Blocks.OB; FB/FC exports still carry it.
if (type != "OB") attrs.Add(new XElement("Namespace"));
```

Regresión cubierta por `BlockXml_ObHasNoNamespaceElement`.

### 11.3 OB: sin Title/Comment por atributo

En V17, `block.SetAttribute("Title", ...)` y `SetAttribute("Comment", ...)` fallan para un OB:

```text
'Title' is not supported by type 'Siemens.Engineering.SW.Blocks.OB'.
```

Usar comentarios en las tags, o documentar la lógica en la guía/README. Para FB/FC se puede intentar `patch_block_texts`, pero no está verificado en vivo para V17.

### 11.4 Tabla de tags

- Cada elemento del `ObjectList` necesita un `ID` entero único.
- `ExternalWritable`: `false` para `%I`, `true` para `%Q` y `%M`.
- `import_tag_table` usa `ImportOptions.Override`: **reemplaza la tabla completa**.
- Para agregar pocas tags con comentario, `create_tag` es más simple y seguro.
- Los comentarios con `<MultilingualText>` pueden fallar si la cultura no coincide con la del proyecto. Si se escribe XML a mano, usar `<ObjectList />` y omitir comentarios, o usar `create_tag`.
- Direcciones válidas: `%I0.0`, `%Q0.0`, `%MW0`, `%MD0`, `%IW0`, etc.

### 11.5 Errores comunes de importación

| Error | Causa | Solución |
|---|---|---|
| `The 'Namespace' attribute ... is not supported` en OB | `<Namespace />` presente en OB | Omitirlo en OB. |
| `Missing 'Namespace' identifier attribute` en GlobalDB | Falta `<Namespace />` o está como atributo XML | Ponerlo como elemento hijo de `<AttributeList>`. |
| `Cannot import multilingual text with culture ...` | `<MultilingualText>` con cultura distinta a la del proyecto | Omitir comentarios o usar la cultura correcta. |
| `Section 'Return' is not valid for this block` | `Return` en un FB | No incluir Return en FB. |
| `Error when calling method 'Import'` genérico | XML malformado o atributo no soportado | Exportar un bloque del mismo tipo desde TIA y comparar; revisar la excepción interna. |
| `'Title'/'Comment' is not supported ... OB` | `patch_block_texts` sobre OB | No aplicar a OB en V17. |

### 11.6 SCL y GlobalDB

- `create_block` crea SCL para FB/FC/OB/GlobalDB.
- El nombre del bloque en la declaración SCL debe coincidir con `name`.
- Para GlobalDB, el parser de miembros es limitado: no soporta initial values, arrays con espacios ni `STRUCT` anidados; esos miembros se pierden silenciosamente. Revisar con `read_block` después de crear.
- Para LAD, usar `create_lad_block`; para FBD/STL, `import_block_xml`.

---

## 12. API HTTP y dashboard

Base: `http://localhost:5000`. JSON en camelCase.

### 12.1 Endpoints principales

| Método | Ruta | Uso |
|---|---|---|
| `GET` | `/` | Dashboard. |
| `GET` | `/api/status` | Estado de conexión; no bloquea. |
| `POST` | `/api/connect` | Adjuntarse a TIA; bloquea hasta aprobación. |
| `GET` | `/api/project/signature` | Índice de bloques/tags y consistencia. |
| `GET` | `/api/project/options` | Paquetes de opción. |
| `POST` | `/api/project/save` | Guardar. |
| `POST` | `/api/project/clone` | Clonar. |
| `GET` | `/api/devices` | Dispositivos. |
| `GET` | `/api/devices/{device}/blocks` | Listar bloques. |
| `GET` | `/api/devices/{device}/blocks/{block}` | Leer bloque. |
| `POST` | `/api/devices/{device}/blocks` | Crear bloque SCL. |
| `POST` | `/api/devices/{device}/blocks/{block}/xml` | Importar XML. |
| `PUT` | `/api/devices/{device}/blocks/{block}/scl` | Actualizar SCL. |
| `POST` | `/api/devices/{device}/blocks/{block}/compile` | Compilar. |
| `POST` | `/api/devices/{device}/blocks/{block}/analyze` | Analizar SCL. |
| `POST` | `/api/devices/{device}/blocks/instance-db` | Crear DB de instancia. |
| `GET` | `/api/devices/{device}/tags` | Listar tablas. |
| `GET` | `/api/devices/{device}/tags/{table}` | Leer tags. |
| `POST` | `/api/devices/{device}/tags/import` | Importar tabla completa. |
| `POST` | `/api/devices/{device}/tags/{table}/rename` | Renombrar en lote. |
| `POST` | `/api/analyze` | Analizar SCL sin bloque. |
| `GET` | `/api/mcp/log` | Últimas llamadas MCP. |
| `POST` | `/mcp` | JSON-RPC MCP. |

- `{device}` debe ser el nombre exacto del device; las rutas con espacios se URL-encodean (`%20`).
- Los errores suelen volver con HTTP 200 y `{error:"..."}`.

### 12.2 HMI Unified

Solo con build `TiaHmiUnified=true` (V18+). En V17 las rutas `/api/devices/{device}/hmi/...` no existen; el router devuelve 404.

---

## 13. Diagnóstico y errores frecuentes

### Conexión

| Síntoma | Causa / solución |
|---|---|
| `No running TIA Portal process found` | Abrir TIA Portal V17 con un proyecto cargado antes de conectar. |
| Error de seguridad / grupo Openness | Agregar la cuenta a `Siemens TIA Openness`, cerrar y volver a iniciar sesión. |
| El diálogo de aprobación no aparece | Puede estar detrás de otras ventanas; revisar la barra de tareas. |
| `connect` queda colgado | Esperar la aprobación; usar timeout ≥90 s. |
| Se conecta a la instancia equivocada | Cerrar otras instancias; `projectPath` no se usa en el dispatch actual. |
| `connected:false` tras un rato | El proceso MCP pudo reiniciarse; volver a conectar. |

### Proyecto y bloques

| Síntoma | Causa / solución |
|---|---|
| `Inconsistent blocks and PLC data types (UDT) cannot be exported` | Compilar todo en TIA con Ctrl+B y reintentar. |
| `Block is not SCL or source could not be read` | `analyze_block` solo aplica a SCL; LAD/FBD/STL no tienen fuente extraíble. |
| `isConsistent:false` tras un cambio | Recompilar el bloque y volver a guardar. |
| No se puede crear un bloque | El PLC está online; Openness exige modo offline. |
| El nombre del device no se encuentra | Usar el nombre del device, no el de la CPU. Ejemplo: `S7-1200 station_1`, no `PLC_1`. |
| `patch_block_texts` falla en OB | V17 no soporta Title/Comment en OB. |
| El proyecto se cerró al salir del MCP | Versión antigua; en esta versión `Dispose` no cierra proyectos adjuntos. Verificar que se esté usando el build nuevo. |

### LAD / XML

| Síntoma | Causa / solución |
|---|---|
| Error genérico `Import` | Revisar la excepción interna; exportar un bloque equivalente desde TIA y comparar. Para OB, verificar ausencia de `<Namespace />`. |
| `Namespace ... not supported` | Quitar `<Namespace />` del OB. |
| La compilación falla por tags inexistentes | Crear las tags antes de importar/compilar. |
| `number` de OB ya usado | Elegir otro número libre (123+). |
| `dryRun` muestra 123 pero el OB real toma otro número | `dryRun` no consulta TIA; pasar número explícito. |
| Rama inválida | Cada `branch` necesita al menos dos caminos no vacíos. |
| `move` falla | Debe ser el último elemento de la red y no puede tener outputs ni estar dentro de una rama. |

### Dashboard / HTTP

| Síntoma | Causa / solución |
|---|---|
| `http://localhost:5000` no responde | El dashboard no está corriendo o el puerto está ocupado. |
| WebView2 no abre | Instalar/actualizar Microsoft Edge WebView2. |
| Claude Desktop no ve herramientas HTTP | Agregar el connector en la UI, no en `claude_desktop_config.json`. |
| `GET /mcp` devuelve 405 | Es el comportamiento esperado para detectar Streamable HTTP. |
| El cliente no ve herramientas | Revisar el perfil (`--profile`), reiniciar el cliente y mirar `/api/mcp/log`. |

### Build

| Síntoma | Causa / solución |
|---|---|
| `Siemens.Engineering.dll not found` | TIA V17 no está en la ruta esperada; ajustar `HintPath`. |
| Errores de `HmiUnified` | Se intentó compilar HMI en V17; usar build por defecto o V18+. |
| `dotnet` no se reconoce | Agregar `C:\Program Files\dotnet` al `PATH` o instalar .NET SDK 8. |
| El exe está bloqueado | Cerrar `TiaPortalDashboard.exe` antes de recompilar. |

---

## 14. Tests y validación

### 14.1 Comandos

```powershell
$env:PATH = "C:\Program Files\dotnet;$env:PATH"
dotnet build .\src\TiaOpennessMcpServer\TiaOpennessMcpServer.csproj -c Release
dotnet test .\tests\LadXmlBuilder.Tests\LadXmlBuilder.Tests.csproj -c Release
```

### 14.2 Cobertura

La suite de 47 tests cubre, entre otros:

- Topología de enclavamiento y ramas paralelas.
- Contactos NC, bobinas set/reset.
- Temporizadores, contadores, comparaciones, flancos.
- Aritmética, `norm_x`/`scale_x`, `sr`/`rs`, `move`.
- Llamadas a FB/FC con y sin parámetros, DB de instancia y multi-instancia.
- Esquemas V17 (`Engineering V17`, `Interface/v4`, `FlgNet/v4`).
- **OB sin `<Namespace />`** (`BlockXml_ObHasNoNamespaceElement`).
- Validación de UIds, wires y errores claros.
- Golden samples de V20 normalizados.

### 14.3 Verificación en vivo recomendada

Después de un cambio:

```text
1. connect_to_tia_portal
2. get_status                    → V17
3. list_devices                  → nombre exacto
4. get_project_signature         → antes
5. crear/modificar
6. compile_block                 → 0 errores
7. get_project_signature         → isConsistent:true
8. save_project
9. get_status                    → isModified:false
10. cerrar el cliente y confirmar que TIA sigue abierto
```

---

## 15. Limitaciones conocidas

1. **WinCC Unified no está disponible en V17.** Las 7 herramientas HMI solo existen en build V18+.
2. **`patch_block_texts` no funciona en OB V17.** Tampoco procesa el array `networks` en el servicio actual.
3. **`connect_to_tia_portal.projectPath` no se usa en el dispatch.** No se puede elegir entre varias instancias.
4. **`appsettings.json` no se carga.** Los defaults están en `TiaOpennessOptions.cs`.
5. **`import_tag_table` reemplaza la tabla completa.**
6. **El parser de GlobalDB SCL es limitado.** Pierde initial values, arrays con espacios y STRUCT anidados.
7. **No se crean bloques con el PLC online.**
8. **El ejemplo de motor no se descarga automáticamente.** Solo se compila y guarda.
9. **V17 exporta `Interface/v5` aunque el builder importe v4.** Es esperado; no “corregir” el builder a v5 por un export.
10. **Los fixtures V20 son históricos.** No editarlos para que coincidan con V17.

---

## 16. Publicar el proyecto en GitHub

### 16.1 Antes de subir

Verificar que no se suban artefactos ni proyectos:

```powershell
Get-ChildItem -Force
Get-ChildItem -Recurse -Directory -Filter bin
Get-ChildItem -Recurse -Directory -Filter obj
```

El `.gitignore` ya excluye:

```text
bin/
obj/
.vs/
*.user
*.suo
*.ap17
*.ap18
*.ap19
*.ap20
*.ap21
AdditionalFiles/
IM/
Logs/
System/
Vci/
XRef/
```

### 16.2 Instalar Git

Si `git --version` falla:

```powershell
winget install --id Git.Git -e --source winget
```

Si no hay `winget`, instalar **Git for Windows** desde <https://git-scm.com/download/win> y reiniciar la terminal.

### 16.3 Crear el repositorio local

```powershell
cd C:\ruta\al\repo\tia-portal-mcp-master
git init -b main
git add .
git status
git commit -m "TIA Portal MCP Server V17"
```

Revisar `git status` antes del commit: no deben aparecer `.ap17`, `bin/`, `obj/` ni archivos de usuario.

### 16.4 Crear el repositorio remoto

Opción A, con GitHub CLI:

```powershell
winget install --id GitHub.cli -e --source winget
gh auth login
gh repo create <usuario>/<repo> --private --source . --remote origin --push
```

Opción B, desde la web:

1. Crear un repositorio vacío en GitHub.
2. Copiar la URL.
3. Ejecutar:

```powershell
git remote add origin https://github.com/<usuario>/<repo>.git
git push -u origin main
```

### 16.5 Recomendaciones para el repositorio

- Agregar un `LICENSE` (el README declara MIT; si se mantiene, incluir el texto MIT).
- Etiquetar la versión: `git tag v17.0.0 && git push --tags`.
- En la descripción del repo: “TIA Portal V17 Openness MCP server — PLC blocks, tags, LAD and project automation”.
- No subir credenciales, tokens, rutas personales ni proyectos `.ap17`.
- Si se publica un fork, actualizar la sección de clonado del README con la URL correcta.

---

## 17. Mapa del proyecto

```text
tia-portal-mcp-master/
├── README.md                         # Presentación y setup rápido
├── TIA_PORTAL_MCP_GUIDE.md           # Esta guía operativa
├── CLAUDE.md                         # Instrucciones para agentes
├── tia-portal-mcp-implementation-plan.md
├── tia-api.http                      # Ejemplos REST
├── docs/
│   ├── user-manual.md
│   ├── what-is-tia-openness.md
│   ├── future-features.md
│   └── lad-samples/                  # Fixtures reales V20 (no editar)
├── src/TiaOpennessMcpServer/
│   ├── Program.cs                    # HTTP, stdio, MCP, routing
│   ├── MainForm.cs                   # Ventana WinForms + WebView2
│   ├── dashboard.html
│   ├── appsettings.json              # No se carga actualmente
│   ├── Services/
│   │   ├── TiaPortalService.cs       # Attach, save, clone, signature, dispose
│   │   ├── HardwareService.cs
│   │   ├── SoftwareService.cs        # Bloques, LAD, compile, XML
│   │   ├── TagService.cs
│   │   ├── SclAnalyzerService.cs
│   │   ├── HmiTagService.cs          # Excluido en V17
│   │   └── HmiScreenService.cs       # Excluido en V17
│   ├── Models/
│   └── Utilities/
│       ├── StaTaskScheduler.cs
│       ├── XmlHelper.cs
│       ├── LadXmlBuilder.cs          # Generación LAD V17
│       └── NetFxPolyfills.cs
└── tests/LadXmlBuilder.Tests/        # 47 tests
```

---

## 18. Checklist rápido

### Antes de conectar

- [ ] TIA Portal V17 abierto con el proyecto `.ap17`.
- [ ] Usuario en `Siemens TIA Openness`.
- [ ] Build Release actualizado.
- [ ] Cliente MCP apuntando al exe correcto con `--mcp-stdio`.

### Después de conectar

- [ ] `get_status` → `connected:true`, `portalVersion:"V17"`.
- [ ] `list_devices` → nombre exacto del device.
- [ ] `get_project_signature` → estado inicial.

### Antes de guardar

- [ ] `compile_block` → 0 errores, 0 warnings.
- [ ] `get_project_signature` → `isConsistent:true` en los bloques tocados.
- [ ] Revisar lógica y direcciones de I/O.
- [ ] `save_project`.
- [ ] `get_status` → `isModified:false`.

### Al terminar

- [ ] El proyecto sigue abierto en TIA.
- [ ] No se descargó nada al PLC sin autorización.
- [ ] No se subieron `.ap17`, `bin/`, `obj/` ni credenciales al repo.

---

### Nota final

Esta guía documenta el estado **V17** del repositorio. Si en el futuro se migra a V18/V19/V20, revisar especialmente:

- Referencias `HintPath` y `TiaHmiUnified`.
- Esquemas SimaticML (`Interface`/`FlgNet`).
- Soporte de `Namespace`, `Title` y `Comment` por tipo de bloque.
- Conteo de herramientas y perfiles.
- Tests golden y fixtures históricos.
