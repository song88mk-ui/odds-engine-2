using System.Security.Cryptography;
using System.Text;

namespace OE.Shared.Utilities
{
    /// <summary>
    /// Produces a hash byte-for-byte identical to the SportsbookDynamic.SOURCE_HASH computed column,
    /// so translations can be looked up by source text (content-keying):
    ///   SOURCE_HASH = CONVERT(varchar(64), HASHBYTES('SHA2_256', SOURCE_TEXT), 2)
    /// SOURCE_TEXT is nvarchar -> hash the UTF-16LE bytes; output is uppercase hex, no "0x".
    /// Mirror of platform-final's Platform.Shared.Utility.TranslationHashing (kept in sync by convention).
    /// </summary>
    public static class TranslationHashing
    {
        public static string SourceHash(string sourceText)
        {
            if (sourceText == null)
                return null;

            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.Unicode.GetBytes(sourceText); // UTF-16LE == SQL nvarchar
                var hash = sha.ComputeHash(bytes);

                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash)
                    sb.Append(b.ToString("X2")); // uppercase hex, matches CONVERT(..., 2)

                return sb.ToString();
            }
        }
    }
}
