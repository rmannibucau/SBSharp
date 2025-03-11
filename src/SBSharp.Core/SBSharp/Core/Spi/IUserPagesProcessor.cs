namespace SBSharp.Core.SBSharp.Core.Spi;

public interface IUserPagesProcessor
{
    public Task Process(IDictionary<string, Page> pages, CancellationToken cancellationToken);
}
