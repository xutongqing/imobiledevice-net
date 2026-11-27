namespace iMobileDevice.Managed.Backup;

using iMobileDevice.Managed.Afc;
using iMobileDevice.Managed.Lockdown;
using iMobileDevice.Managed.Plist;

internal static class BackupInfo
{
    public static async Task<PlistDictionary> CreateAsync(LockdownClient lockdown, AfcClient afc, CancellationToken token)
    {
        var values = await lockdown.GetAllValuesAsync(cancellationToken: token).ConfigureAwait(false);
        string id = lockdown.Device.SerialNumber;
        var info = new PlistDictionary
        {
            ["Target Identifier"] = id, ["Unique Identifier"] = id.ToUpperInvariant(), ["Target Type"] = "Device",
            ["GUID"] = Guid.NewGuid().ToString("N").ToUpperInvariant(), ["Last Backup Date"] = DateTimeOffset.UtcNow,
        };
        foreach (var (key, label) in new[] { ("DeviceName", "Device Name"), ("DeviceName","Display Name"), ("SerialNumber","Serial Number"),
            ("ProductVersion","Product Version"), ("ProductType","Product Type"), ("BuildVersion","Build Version"),
            ("PhoneNumber","Phone Number"), ("IntegratedCircuitCardIdentity","ICCID"),
            ("InternationalMobileEquipmentIdentity","IMEI"), ("MobileEquipmentIdentifier","MEID") })
            if (values.TryGetValue(key, out var value)) info[label] = value;
        var settings = await lockdown.GetAllValuesAsync("com.apple.iTunes", token).ConfigureAwait(false);
        if (settings.Count > 0) info["iTunes Settings"] = settings;
        info["iTunes Version"] = await lockdown.GetStringValueAsync("MinITunesVersion", "com.apple.mobile.iTunes", token).ConfigureAwait(false) ?? "10.0.1";

        var files = new PlistDictionary();
        foreach (var name in new[] { "ApertureAlbumPrefs", "IC-Info.sidb", "IC-Info.sidv", "PhotosFolderAlbums", "PhotosFolderName",
            "PhotosFolderPrefs", "VoiceMemos.plist", "iPhotoAlbumPrefs", "iTunesApplicationIDs", "iTunesPrefs", "iTunesPrefs.plist" })
        {
            try { files[name] = await afc.ReadAllBytesAsync("/iTunes_Control/iTunes/" + name, token).ConfigureAwait(false); }
            catch (AfcException ex) when (ex.ErrorCode == AfcErrorCode.ObjectNotFound) { }
        }
        info["iTunes Files"] = files;
        try { info["iBooks Data 2"] = await afc.ReadAllBytesAsync("/Books/iBooksData2.plist", token).ConfigureAwait(false); }
        catch (AfcException ex) when (ex.ErrorCode == AfcErrorCode.ObjectNotFound) { }

        await using var service = await lockdown.ConnectToServiceAsync("com.apple.mobile.installation_proxy", cancellationToken: token).ConfigureAwait(false);
        var wire = new BackupWire(service);
        var names = new PlistArray();
        var apps = new PlistDictionary();
        await wire.SendAsync(new PlistDictionary
        {
            ["Command"] = "Browse",
            ["ClientOptions"] = new PlistDictionary
            {
                ["ApplicationType"] = "User",
                ["ReturnAttributes"] = new PlistArray { "CFBundleIdentifier", "ApplicationSINF", "iTunesMetadata" },
            },
        }, token).ConfigureAwait(false);
        while (true)
        {
            var reply = (await wire.ReceiveAsync(token).ConfigureAwait(false)).AsDictionary();
            if (reply.GetString("Error") is string error) throw new IOException("Reading installed applications failed: " + error);
            if (reply.GetArray("CurrentList") is { } list)
            {
                foreach (var entry in list)
                {
                    var app = entry.AsDictionary();
                    if (app.GetString("CFBundleIdentifier") is not string bundle) continue;
                    names.Add(bundle);
                    if (app.TryGetValue("ApplicationSINF", out var sinf) && app.TryGetValue("iTunesMetadata", out var metadata))
                        apps[bundle] = new PlistDictionary { ["ApplicationSINF"] = sinf, ["iTunesMetadata"] = metadata };
                }
            }
            if (reply.GetString("Status") == "Complete") break;
        }
        if (apps.Count != 0)
        {
            await using var springboard = await lockdown.ConnectToServiceAsync("com.apple.springboardservices", cancellationToken: token).ConfigureAwait(false);
            var icons = new BackupWire(springboard);
            foreach (var (bundle, app) in apps)
            {
                await icons.SendAsync(new PlistDictionary { ["command"] = "getIconPNGData", ["bundleId"] = bundle }, token).ConfigureAwait(false);
                var response = (await icons.ReceiveAsync(token).ConfigureAwait(false)).AsDictionary();
                if (response.GetData("pngData") is { } png) app.AsDictionary()["PlaceholderIcon"] = png;
            }
        }
        info["Installed Applications"] = names;
        info["Applications"] = apps;
        return info;
    }

    public static async Task SetRestoreAppsAsync(AfcClient afc, PlistDictionary? applications, CancellationToken token)
    {
        const string path = "/iTunesRestore/RestoreApplications.plist";
        if (applications is null || applications.Count == 0)
        {
            try { await afc.RemovePathAsync(path, token).ConfigureAwait(false); }
            catch (AfcException ex) when (ex.ErrorCode == AfcErrorCode.ObjectNotFound) { }
            return;
        }

        try { await afc.MakeDirectoryAsync("/iTunesRestore", token).ConfigureAwait(false); }
        catch (AfcException ex) when (ex.ErrorCode == AfcErrorCode.ObjectExists) { }
        await afc.WriteAllBytesAsync(path, PlistSerializer.WriteXml(applications), token).ConfigureAwait(false);
    }
}
