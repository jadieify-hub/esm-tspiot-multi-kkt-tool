using System.Threading;
using System.Threading.Tasks;
using EsmTspiot.Shared.Models;

namespace EsmTspiot.Shared.Services
{
    public interface ITspiotApiClient
    {
        Task<ApiResponse> GetInstancesAsync(string baseUrl, CancellationToken cancellationToken);
        Task<ApiResponse> GetDkktListAsync(string baseUrl, CancellationToken cancellationToken);
        Task<ApiResponse> GetInstanceAsync(string baseUrl, string id, CancellationToken cancellationToken);
        Task<ApiResponse> GetSettingsAsync(string baseUrl, string id, CancellationToken cancellationToken);
        Task<ApiResponse> AddInstanceAsync(string baseUrl, AddTspiotRequest request, CancellationToken cancellationToken);
        Task<ApiResponse> RegisterInstanceAsync(string baseUrl, RegisterTspiotRequest request, CancellationToken cancellationToken);
        Task<ApiResponse> DeleteInstanceAsync(string baseUrl, string id, CancellationToken cancellationToken);
    }
}
