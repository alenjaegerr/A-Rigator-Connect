using A_Rigator_Controller.Models;

namespace A_Rigator_Controller.Services;

public interface IIrrigationProfileCatalogService
{
    Task<IReadOnlyList<IrrigationProfile>> GetPublicProfilesAsync(CancellationToken cancellationToken = default);
}
