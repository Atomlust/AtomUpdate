using AtomUpdate.Core.Constants;
using AtomUpdate.Core.Models.Manage;

namespace AtomUpdate.Web.Client.Services;

/// <summary>管理后台时间显示时钟：把服务端存的 UTC 时间统一换算成平台设置的展示时区。
///
/// 为什么不用 DateTimeOffset.ToLocalTime()：那转的是**服务器**时区，而容器里通常是 UTC，
/// 用户看到的全部时间会整体少 8 小时；这里改为读 display.time_zone 设置（默认东八区）。
/// 时间数据本身仍按 UTC 存储与传输，只在渲染时换算，不影响 API 契约与时序判断。</summary>
public sealed class DisplayClock(ApiClient api)
{
    private const string FullFormat = "yyyy-MM-dd HH:mm:ss";
    private const string MinuteFormat = "yyyy-MM-dd HH:mm";

    private TimeZoneInfo _timeZone = DisplayTimeZone.Resolve(null);
    private string _timeZoneId = DisplayTimeZone.DefaultTimeZoneId;
    private Task? _loading;

    /// <summary>当前展示时区（IANA 标识）。</summary>
    public string TimeZoneId => _timeZoneId;

    /// <summary>确保设置已加载（幂等，并发调用共享同一次请求）；未登录或失败时保持默认东八区。</summary>
    public async Task EnsureLoadedAsync()
    {
        if (_loading is not null)
        {
            await _loading;
            return;
        }

        _loading = LoadAsync();
        await _loading;
        _loading = null;
    }

    private async Task LoadAsync()
    {
        // 走 /display-timezone 而非 /settings：后者要平台管理员权限，租户成员会 403 而退回默认时区
        var result = await api.GetAsync<DisplayTimeZoneDto>(ApiRoutes.ManageDisplayTimeZone);
        if (result.IsSuccess && result.Data is not null)
        {
            Apply(result.Data.TimeZoneId);
        }
    }

    /// <summary>设置页保存成功后即时生效，无需刷新页面。</summary>
    public void Apply(string? timeZoneId)
    {
        _timeZone = DisplayTimeZone.Resolve(timeZoneId);
        _timeZoneId = DisplayTimeZone.IsValid(timeZoneId)
            ? timeZoneId!.Trim()
            : DisplayTimeZone.DefaultTimeZoneId;
    }

    /// <summary>换算到展示时区（保留偏移量，便于用户核对）。</summary>
    public DateTimeOffset ToDisplay(DateTimeOffset value) =>
        TimeZoneInfo.ConvertTime(value, _timeZone);

    /// <summary>按展示时区格式化；format 为 null 时用「年-月-日 时:分:秒」。</summary>
    public string Format(DateTimeOffset value, string? format = null) =>
        ToDisplay(value).ToString(format ?? FullFormat, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>可空版本；null 输出占位符（默认 "-"）。</summary>
    public string Format(DateTimeOffset? value, string? format = null, string placeholder = "-") =>
        value is null ? placeholder : Format(value.Value, format);

    /// <summary>仅到分钟（列表页常用）。</summary>
    public string FormatMinute(DateTimeOffset value) => Format(value, MinuteFormat);
}
