using System.Text;

namespace DnRelay.Utilities;

static class OutputEncoding
{
    static OutputEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static Encoding Utf8 { get; } = Encoding.UTF8;
}
