﻿#if ANDROID
using Android.App;
using Plugin.FirebasePushNotifications.Platforms.Channels;

namespace Plugin.FirebasePushNotifications.Platforms
{
    public class FirebasePushNotificationAndroidOptions
    {
        private NotificationChannelRequest[] notificationChannels = Array.Empty<NotificationChannelRequest>();
        private Type notificationActivityType;

        /// <summary>
        /// The Activity which handles incoming push notifications.
        /// Typically, this is <c>typeof(MainActivity)</c>.
        /// </summary>
        public virtual Type NotificationActivityType
        {
            get => this.notificationActivityType;
            set
            {
                if (!typeof(Activity).IsAssignableFrom(value))
                {
                    throw new ArgumentException($"{nameof(this.NotificationActivityType)} must be of type {typeof(Activity).FullName}");
                }

                this.notificationActivityType = value;
            }
        }

        public virtual NotificationChannelRequest[] NotificationChannels
        {
            get => this.notificationChannels;
            set
            {
                EnsureNotificationChannelRequests(
                    value,
                    $"{nameof(FirebasePushNotificationOptions)}.{nameof(FirebasePushNotificationOptions.Android)}",
                    nameof(this.NotificationChannels));

                this.notificationChannels = value;
            }
        }

        internal static void EnsureNotificationChannelRequests(NotificationChannelRequest[] notificationChannels, string source, string paramName)
        {
            if (notificationChannels == null)
            {
                throw new ArgumentNullException(paramName, $"{source} must not be null");
            }

            var duplicateChannelIds = notificationChannels
               .Select(c => c.ChannelId) //.Concat(new[] { DefaultNotificationChannel.ChannelId })
               .GroupBy(c => c)
               .Where(g => g.Count() > 1)
               .Select(g => g.Key);

            if (duplicateChannelIds.Any())
            {
                throw new ArgumentException(
                    $"{source} contains {nameof(NotificationChannelRequest)} with duplicate {nameof(NotificationChannelRequest.ChannelId)}: " +
                    $"[{string.Join(", ", duplicateChannelIds.Select(id => $"\"{id}\""))}]",
                    paramName);
            }

            var defaultNotificationChannels = notificationChannels.Where(c => c.IsDefault && c.IsActive).ToArray();
            if (defaultNotificationChannels.Length > 1)
            {
                throw new ArgumentException(
                    $"{source} contains more than one active {nameof(NotificationChannelRequest)} with {nameof(NotificationChannelRequest.IsDefault)}=true" +
                    $"[{string.Join(", ", defaultNotificationChannels.Select(c => $"\"{c.ChannelId}\""))}]",
                    paramName);
            }
            else if (defaultNotificationChannels.Length < 1)
            {
                throw new ArgumentException(
                    $"{source} does not contain any active {nameof(NotificationChannelRequest)} with {nameof(NotificationChannelRequest.IsDefault)}=true",
                    paramName);
            }
        }
    }
}
#endif