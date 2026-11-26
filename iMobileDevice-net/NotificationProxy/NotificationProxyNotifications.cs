// <copyright file="NotificationProxyNotifications.cs" company="Quamotion">
// Copyright (c) 2016-2026 Quamotion. All rights reserved.
// </copyright>
#pragma warning disable 1591

namespace iMobileDevice.NotificationProxy
{
    /// <summary>
    /// Standard notification names defined in <c>libimobiledevice/notification_proxy.h</c>.
    /// </summary>
    public static class NotificationProxyNotifications
    {
        public const string ServiceName = "com.apple.mobile.notification_proxy";

        // Notifications that can be sent (client -> device)
        public const string SyncWillStart = "com.apple.itunes-mobdev.syncWillStart";
        public const string SyncDidStart = "com.apple.itunes-mobdev.syncDidStart";
        public const string SyncDidFinish = "com.apple.itunes-mobdev.syncDidFinish";
        public const string SyncLockRequest = "com.apple.itunes-mobdev.syncLockRequest";

        // Notifications that can be received (device -> client)
        public const string SyncCancelRequest = "com.apple.itunes-client.syncCancelRequest";
        public const string SyncSuspendRequest = "com.apple.itunes-client.syncSuspendRequest";
        public const string SyncResumeRequest = "com.apple.itunes-client.syncResumeRequest";
        public const string PhoneNumberChanged = "com.apple.mobile.lockdown.phone_number_changed";
        public const string DeviceNameChanged = "com.apple.mobile.lockdown.device_name_changed";
        public const string TimezoneChanged = "com.apple.mobile.lockdown.timezone_changed";
        public const string TrustedHostAttached = "com.apple.mobile.lockdown.trusted_host_attached";
        public const string HostDetached = "com.apple.mobile.lockdown.host_detached";
        public const string HostAttached = "com.apple.mobile.lockdown.host_attached";
        public const string RegistrationFailed = "com.apple.mobile.lockdown.registration_failed";
        public const string ActivationState = "com.apple.mobile.lockdown.activation_state";
        public const string BrickState = "com.apple.mobile.lockdown.brick_state";
        public const string DiskUsageChanged = "com.apple.mobile.lockdown.disk_usage_changed";
        public const string DsDomainChanged = "com.apple.mobile.data_sync.domain_changed";
        public const string BackupDomainChanged = "com.apple.mobile.backup.domain_changed";
        public const string AppInstalled = "com.apple.mobile.application_installed";
        public const string AppUninstalled = "com.apple.mobile.application_uninstalled";
        public const string DevImageMounted = "com.apple.mobile.developer_image_mounted";
        public const string AttemptActivation = "com.apple.springboard.attemptactivation";
        public const string ItdbPrepDidEnd = "com.apple.itdbprep.notification.didEnd";
        public const string LanguageChanged = "com.apple.language.changed";
        public const string AddressBookPrefChanged = "com.apple.AddressBook.PreferenceChanged";
    }
}
