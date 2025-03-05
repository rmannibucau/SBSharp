using NAsciidoc.Renderer;

namespace SBSharp.Core.SBSharp.Core.Rendering;

public interface IRendererFactory
{
    AsciidoctorLikeHtmlRenderer Create(AsciidoctorLikeHtmlRenderer.Configuration configuration);
}
