using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.DigitalOcean.Runners.OpenApiClient.Utils.Abstract;

public interface IFileOperationsUtil
{
    /// <summary>
    /// Runs the OpenAPI client regeneration workflow, including cleanup and post-processing.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the full processing workflow has finished.</returns>
    ValueTask Process(CancellationToken cancellationToken = default);
}
