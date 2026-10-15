using System.Net.Http;
using System.Text.Json;
using Android.Content;
using Microsoft.Maui.ApplicationModel;

namespace CardexMobile.Services;

public record MobileUpdateInfo(string Version, string DownloadUrl);

// Équivalent mobile de Cardwatcher/Services/UpdateService.cs (même source — releases GitHub,
// même comparaison de version) mais l'installation diffère forcément : pas d'auto-remplacement
// d'exe comme sur desktop, un APK sideloadé se met à jour via l'installeur système Android
// (FileProvider + Intent ACTION_VIEW), qui demande à l'utilisateur de confirmer.
public class MobileUpdateService
{
    private const string ApiUrl = "https://api.github.com/repos/Darumacho/CardexMobile/releases/latest";
    private const string AssetName = "CardexMobile.apk";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public MobileUpdateService()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("CardexMobile");
    }

    public async Task<MobileUpdateInfo?> CheckAsync()
    {
        try
        {
            var json = await _http.GetStringAsync(ApiUrl);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.GetProperty("tag_name").GetString() ?? "";
            var versionStr = tag.TrimStart('v');
            if (!Version.TryParse(versionStr, out var remote)) return null;

            if (Version.TryParse(AppInfo.Current.VersionString, out var current))
            {
                // Normalise en Major.Minor.Build pour éviter les soucis de comparaison de la
                // révision à -1 (même logique que le pendant desktop).
                var r = new Version(remote.Major, Math.Max(0, remote.Minor), Math.Max(0, remote.Build));
                var c = new Version(current.Major, Math.Max(0, current.Minor), Math.Max(0, current.Build));
                if (r <= c) return null;
            }

            var assets = root.GetProperty("assets");
            string? downloadUrl = null;
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() == AssetName)
                {
                    downloadUrl = asset.GetProperty("browser_download_url").GetString();
                    break;
                }
            }

            return downloadUrl is not null ? new MobileUpdateInfo(tag, downloadUrl) : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task DownloadAndInstallAsync(MobileUpdateInfo info, IProgress<int>? progress = null)
    {
        var context = Android.App.Application.Context;
        var apkPath = Path.Combine(context.CacheDir!.AbsolutePath, "CardexMobile_update.apk");

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CardexMobile");

        using var response = await http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1;
        await using var src = await response.Content.ReadAsStreamAsync();
        await using (var dst = new FileStream(apkPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920))
        {
            var buf = new byte[81920];
            long read = 0;
            int n;
            while ((n = await src.ReadAsync(buf)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n));
                read += n;
                if (total > 0) progress?.Report((int)(read * 100 / total));
            }
        }

        // L'installeur système a besoin d'une content:// URI (pas file://, interdit depuis
        // Android 7+) — FileProvider déclaré dans AndroidManifest.xml, chemin autorisé dans
        // Platforms/Android/Resources/xml/file_paths.xml. REQUEST_INSTALL_PACKAGES permet à l'app
        // de proposer l'install ; le premier sideload sur un appareil donné demande quand même à
        // l'utilisateur d'autoriser "installer des apps inconnues" pour Cardex, via un écran
        // système automatique — rien à coder côté app pour ça.
        var apkFile = new Java.IO.File(apkPath);
        var apkUri = AndroidX.Core.Content.FileProvider.GetUriForFile(context, $"{context.PackageName}.fileprovider", apkFile);

        var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(apkUri, "application/vnd.android.package-archive");
        intent.SetFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
        context.StartActivity(intent);
    }
}
