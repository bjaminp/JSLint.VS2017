#pragma warning disable 1591

namespace JSLint.MSBuild.Abstractions
{
    using JSLint.VS2010.OptionClasses;

    public interface IJSLintFactory
    {
        IIgnoreErrorSectionsHandler CreateIgnoreErrorSectionsHandler(string contents, Options options);

        IJSLinter CreateLinter();

        IOptionsProvider CreateOptionsProvider(string filePath);
    }
}
