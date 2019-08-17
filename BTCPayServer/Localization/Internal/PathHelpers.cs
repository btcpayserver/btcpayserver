using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace BTCPayServer.Localization.Internal
{
    public static class PathHelpers
    {
        // http://codebuckets.com/2017/10/19/getting-the-root-directory-path-for-net-core-applications/
        public static string GetApplicationRoot()
        {
            var exePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().CodeBase);
            var appPathMatcher = new Regex(@"(?<!fil)[A-Za-z]:\\+[\S\s]*?(?=\\+bin)");
            var appRoot = appPathMatcher.Match(exePath).Value;

            return appRoot;
        }
    }
}
