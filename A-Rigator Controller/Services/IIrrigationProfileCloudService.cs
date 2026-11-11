using A_Rigator_Controller.Models;

namespace A_Rigator_Controller.Services;

public interface IIrrigationProfileCloudService
{
    Task<IReadOnlyList<IrrigationProfile>> DownloadProfilesAsync(CancellationToken cancellationToken = default);
}
