using System.Text.RegularExpressions;
using System.Xml.Linq;
using TiaOpennessMcpServer.Utilities;

namespace LadTests;

/// <summary>
/// Loads the fixtures in docs/lad-samples. Those files were exported by TIA Portal V20 and
/// therefore use the v5 schemas; the builder targets V17's v4 schemas. The tests compare
/// wiring topology and block structure, which are independent of the schema version, so the
/// sample namespaces are normalised to the versions the builder currently emits.
/// </summary>
internal static class GoldenSamples
{
    public static XDocument Load(string path)
    {
        var text = File.ReadAllText(path);

        var flgVersion = LadXmlBuilder.FlgNs.NamespaceName[(LadXmlBuilder.FlgNs.NamespaceName.LastIndexOf('/') + 1)..];
        var ifaceVersion = LadXmlBuilder.IfaceNs.NamespaceName[(LadXmlBuilder.IfaceNs.NamespaceName.LastIndexOf('/') + 1)..];

        text = Regex.Replace(text, @"/NetworkSource/FlgNet/v\d+", "/NetworkSource/FlgNet/" + flgVersion);
        text = Regex.Replace(text, @"/SW/Interface/v\d+", "/SW/Interface/" + ifaceVersion);

        return XDocument.Parse(text);
    }
}
