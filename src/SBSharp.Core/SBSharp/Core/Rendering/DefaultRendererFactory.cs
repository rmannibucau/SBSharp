using NAsciidoc.Renderer;
using SBSharp.Core.Asciidoc;
using SBSharp.Core.Configuration;

namespace SBSharp.Core.SBSharp.Core.Rendering;

public class DefaultRendererFactory(SBSharpConfiguration appConfiguration) : IRendererFactory
{
    public AsciidoctorLikeHtmlRenderer Create(AsciidoctorLikeHtmlRenderer.Configuration options) =>
        appConfiguration.Output.UseBootstrap
            ? new BootstrapRender(options)
            : new AsciidoctorLikeHtmlRenderer(options);
}
