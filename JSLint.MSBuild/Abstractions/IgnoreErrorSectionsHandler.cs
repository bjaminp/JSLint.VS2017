#pragma warning disable 1591

namespace JSLint.MSBuild.Abstractions
{
    using JSLint.VS2010.OptionClasses;

    public class IgnoreErrorSectionsHandler : JSLint.VS2010.LinterBridge.IgnoreErrorSectionsHandler, IIgnoreErrorSectionsHandler
    {
        public IgnoreErrorSectionsHandler(string contents, Options options = null)
            : base(contents, options)
        {
        }
    }
}
