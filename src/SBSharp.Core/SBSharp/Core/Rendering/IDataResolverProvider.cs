using NAsciidoc.Renderer;

namespace SBSharp.Core.SBSharp.Core.Rendering;

public interface IDataResolverProvider
{
    DataResolver? Create();
}
