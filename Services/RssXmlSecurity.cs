using System.Xml;

namespace Task_Flyout.Services
{
    internal static class RssXmlSecurity
    {
        public static XmlReaderSettings CreateReaderSettings(long maxCharactersInDocument)
            => new()
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersFromEntities = 0,
                MaxCharactersInDocument = maxCharactersInDocument
            };
    }
}
