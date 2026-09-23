using System.Threading;
using System.Threading.Tasks;
using PictureManager.Application.Common;

namespace PictureManager.Application.Settings;

public interface ISettingsService
{
    Task<SettingsDto> GetAsync(CancellationToken cancellationToken = default);

    Task<Result<SettingsSaveResult>> UpdateAsync(SettingsInput input, CancellationToken cancellationToken = default);
}
