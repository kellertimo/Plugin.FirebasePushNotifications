﻿using Foundation;
using Microsoft.Extensions.Logging;
using UIKit;
using UserNotifications;

namespace Plugin.FirebasePushNotifications.Platforms
{
    /// <summary>
    /// Implementation of <see cref="IFirebasePushNotification"/>
    /// for iOS.
    /// </summary>
    public partial class FirebasePushNotificationManager : FirebasePushNotificationManagerBase, IFirebasePushNotification
    {
        private readonly Queue<Tuple<string, bool>> pendingTopics = new Queue<Tuple<string, bool>>();
        private bool hasToken = false;
        private bool disposed;

        public FirebasePushNotificationManager()
            : base()
        {
        }

        /// <inheritdoc />
        public string Token
        {
            get
            {
                var fcmToken = Firebase.CloudMessaging.Messaging.SharedInstance?.FcmToken;
                if (!string.IsNullOrEmpty(fcmToken))
                {
                    return fcmToken;
                }
                else
                {
                    return this.preferences.Get<string>(Constants.Preferences.TokenKey);
                }
            }
        }

        /// <inheritdoc />
        protected override void RegisterNotificationCategoriesPlatform(NotificationCategory[] notificationCategories)
        {
            var unNotificationCategories = new List<UNNotificationCategory>();

            foreach (var notificationCategory in notificationCategories)
            {
                var notificationActions = new List<UNNotificationAction>();

                foreach (var action in notificationCategory.Actions)
                {
                    var notificationActionType = GetUNNotificationActionOptions(action.Type);
                    var notificationAction = UNNotificationAction.FromIdentifier(action.Id, action.Title, notificationActionType);
                    notificationActions.Add(notificationAction);
                }

                // Create UNNotificationCategory
                var options = notificationCategory.Type == NotificationCategoryType.Dismiss
                    ? UNNotificationCategoryOptions.CustomDismissAction
                    : UNNotificationCategoryOptions.None;

                var unNotificationCategory = UNNotificationCategory.FromIdentifier(
                    identifier: notificationCategory.CategoryId,
                    actions: notificationActions.ToArray(),
                    intentIdentifiers: Array.Empty<string>(),
                    options);
                unNotificationCategories.Add(unNotificationCategory);
            }

            // Register categories
            var notificationCategoriesSet = new NSSet<UNNotificationCategory>(unNotificationCategories.ToArray());
            UNUserNotificationCenter.Current.SetNotificationCategories(notificationCategoriesSet);
        }

        /// <inheritdoc />
        protected override void ClearNotificationCategoriesPlatform()
        {
            var categories = new NSSet<UNNotificationCategory>(Array.Empty<UNNotificationCategory>());
            UNUserNotificationCenter.Current.SetNotificationCategories(categories);
        }

        private static UNNotificationActionOptions GetUNNotificationActionOptions(NotificationActionType type)
        {
            UNNotificationActionOptions notificationActionType;

            switch (type)
            {
                case NotificationActionType.AuthenticationRequired:
                    notificationActionType = UNNotificationActionOptions.AuthenticationRequired;
                    break;
                case NotificationActionType.Destructive:
                    notificationActionType = UNNotificationActionOptions.Destructive;
                    break;
                case NotificationActionType.Foreground:
                    notificationActionType = UNNotificationActionOptions.Foreground;
                    break;
                default:
                    notificationActionType = UNNotificationActionOptions.None;
                    break;
            }

            return notificationActionType;
        }

        /// <inheritdoc />
        protected override void ConfigurePlatform(FirebasePushNotificationOptions options)
        {
            var firebaseMessaging = Firebase.CloudMessaging.Messaging.SharedInstance;

            if (firebaseMessaging == null)
            {
                var sharedInstanceNullErrorMessage = "Firebase.CloudMessaging.Messaging.SharedInstance is null";
                this.logger.LogError(sharedInstanceNullErrorMessage);
                throw new NullReferenceException(sharedInstanceNullErrorMessage);
            }

            if (firebaseMessaging.Delegate != null)
            {
                this.logger.LogWarning("Firebase.CloudMessaging.Messaging.SharedInstance.Delegate is already set");
            }
            else
            {
                firebaseMessaging.Delegate = new MessagingDelegateImpl(this.DidReceiveRegistrationToken);
            }

            if (UNUserNotificationCenter.Current.Delegate != null)
            {
                this.logger.LogWarning("UNUserNotificationCenter.Current.Delegate is already set");
            }
            else
            {
                UNUserNotificationCenter.Current.Delegate = new UNUserNotificationCenterDelegateImpl(
                    this.DidReceiveNotificationResponse,
                    this.WillPresentNotification);
            }

            firebaseMessaging.AutoInitEnabled = options.AutoInitEnabled;
        }

        /// <inheritdoc />
        public async Task RegisterForPushNotificationsAsync()
        {
            this.logger.LogDebug("RegisterForPushNotificationsAsync");

            Firebase.CloudMessaging.Messaging.SharedInstance.AutoInitEnabled = true;

            var authOptions = UNAuthorizationOptions.Alert | UNAuthorizationOptions.Badge | UNAuthorizationOptions.Sound;
            var (granted, error) = await UNUserNotificationCenter.Current.RequestAuthorizationAsync(authOptions);
            if (error != null)
            {
                var exception = new Exception("RegisterForPushNotificationsAsync failed with exception", new NSErrorException(error));
                this.logger.LogError(exception, exception.Message);
                throw exception;
            }
            else if (!granted)
            {
                this.logger.LogWarning("RegisterForPushNotificationsAsync: Push notification permission denied by user");
            }
            else
            {
                await MainThread.InvokeOnMainThreadAsync(UIApplication.SharedApplication.RegisterForRemoteNotifications);
            }
        }

        /// <inheritdoc />
        public Task UnregisterForPushNotificationsAsync()
        {
            this.logger.LogDebug("UnregisterForPushNotificationsAsync");

            if (this.hasToken)
            {
                this.UnsubscribeAllTopics();
                this.hasToken = false;
            }

            Firebase.CloudMessaging.Messaging.SharedInstance.AutoInitEnabled = false;

            //if (Firebase.CloudMessaging.Messaging.SharedInstance.Delegate is MessagingDelegateImpl)
            //{
            //    Firebase.CloudMessaging.Messaging.SharedInstance.Delegate = null;
            //}

            UIApplication.SharedApplication.UnregisterForRemoteNotifications();

            this.preferences.Remove(Constants.Preferences.TokenKey);

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public void RegisteredForRemoteNotifications(NSData deviceToken)
        {
            this.logger.LogDebug("RegisteredForRemoteNotifications");

            Firebase.CloudMessaging.Messaging.SharedInstance.ApnsToken = deviceToken;
        }

        /// <inheritdoc />
        public void FailedToRegisterForRemoteNotifications(NSError error)
        {
            this.logger.LogError(new NSErrorException(error), "FailedToRegisterForRemoteNotifications");
        }

        /// <inheritdoc />
        public void DidReceiveRemoteNotification(NSDictionary userInfo)
        {
            this.logger.LogDebug("DidReceiveRemoteNotification");

            this.DidReceiveRemoteNotificationInternal(userInfo);
        }

        /// <inheritdoc />
        public void DidReceiveRemoteNotification(UIApplication application, NSDictionary userInfo, Action<UIBackgroundFetchResult> completionHandler)
        {
            this.logger.LogDebug("DidReceiveRemoteNotification(UIApplication, NSDictionary, Action<UIBackgroundFetchResult>)");

            // If you are receiving a notification message while your app is in the background,
            // this callback will not be fired 'till the user taps on the notification launching the application.

            // If you disable method swizzling, you'll need to call this method. 
            // This lets FCM track message delivery and analytics, which is performed
            // automatically with method swizzling enabled.
            this.DidReceiveRemoteNotificationInternal(userInfo);

            completionHandler(UIBackgroundFetchResult.NewData);
        }

        private void DidReceiveRemoteNotificationInternal(NSDictionary userInfo)
        {
            Firebase.CloudMessaging.Messaging.SharedInstance.AppDidReceiveMessage(userInfo);
            var data = GetParameters(userInfo);
            this.HandleNotificationReceived(data);
        }

        private void WillPresentNotification(UNUserNotificationCenter center, UNNotification notification, Action<UNNotificationPresentationOptions> completionHandler)
        {
            var data = GetParameters(notification.Request.Content.UserInfo);
            var notificationPresentationOptions = GetNotificationPresentationOptions(data);
            this.logger.LogDebug($"WillPresentNotification: UNNotificationPresentationOptions={notificationPresentationOptions}");

            this.HandleNotificationReceived(data);

            completionHandler(notificationPresentationOptions);
        }

        private static UNNotificationPresentationOptions GetNotificationPresentationOptions(IDictionary<string, object> data)
        {
            var notificationPresentationOptions = UNNotificationPresentationOptions.None;

            if (data.TryGetValue("priority", out var priority) && ($"{priority}".ToLower() is "high" or "max"))
            {
                if (UIDevice.CurrentDevice.CheckSystemVersion(14, 0))
                {
                    if (!notificationPresentationOptions.HasFlag(UNNotificationPresentationOptions.List | UNNotificationPresentationOptions.Banner))
                    {
                        notificationPresentationOptions |= UNNotificationPresentationOptions.List | UNNotificationPresentationOptions.Banner;
                    }
                }
                else
                {
                    if (!notificationPresentationOptions.HasFlag(UNNotificationPresentationOptions.Alert))
                    {
                        notificationPresentationOptions |= UNNotificationPresentationOptions.Alert;
                    }
                }
            }
            else if ($"{priority}".ToLower() is "default" or "low" or "min")
            {
                if (UIDevice.CurrentDevice.CheckSystemVersion(14, 0))
                {
                    if (!notificationPresentationOptions.HasFlag(UNNotificationPresentationOptions.List | UNNotificationPresentationOptions.Banner))
                    {
                        notificationPresentationOptions &= UNNotificationPresentationOptions.List | UNNotificationPresentationOptions.Banner;
                    }
                }
                else
                {
                    if (!notificationPresentationOptions.HasFlag(UNNotificationPresentationOptions.Alert))
                    {
                        notificationPresentationOptions &= UNNotificationPresentationOptions.Alert;
                    }
                }
            }

            return notificationPresentationOptions;
        }

        private static IDictionary<string, object> GetParameters(NSDictionary data)
        {
            var parameters = new Dictionary<string, object>();

            var keyAps = new NSString("aps");
            var keyAlert = new NSString("alert");

            foreach (var val in data)
            {
                if (val.Key.Equals(keyAps))
                {
                    if (data.ValueForKey(keyAps) is NSDictionary aps)
                    {
                        foreach (var apsVal in aps)
                        {
                            if (apsVal.Value is NSDictionary)
                            {
                                if (apsVal.Key.Equals(keyAlert))
                                {
                                    foreach (var alertVal in apsVal.Value as NSDictionary)
                                    {
                                        parameters.Add($"aps.alert.{alertVal.Key}", $"{alertVal.Value}");
                                    }
                                }
                            }
                            else
                            {
                                parameters.Add($"aps.{apsVal.Key}", $"{apsVal.Value}");
                            }

                        }
                    }
                }
                else
                {
                    parameters.Add($"{val.Key}", $"{val.Value}");
                }
            }

            return parameters;
        }

        /// <inheritdoc />
        public void SubscribeTopics(string[] topics)
        {
            foreach (var t in topics)
            {
                this.SubscribeTopic(t);
            }
        }

        /// <inheritdoc />
        public void SubscribeTopic(string topic)
        {
            if (topic == null)
            {
                throw new ArgumentNullException(nameof(topic), "Topic must not be null");
            }

            if (topic == string.Empty)
            {
                throw new ArgumentException("Topic must not be empty", nameof(topic));
            }

            if (!this.hasToken)
            {
                this.pendingTopics.Enqueue(new Tuple<string, bool>(topic, true));
                return;
            }

            var subscribedTopics = new HashSet<string>(this.SubscribedTopics);
            if (!subscribedTopics.Contains(topic))
            {
                this.logger.LogDebug($"Subscribe: topic=\"{topic}\"");

                Firebase.CloudMessaging.Messaging.SharedInstance.Subscribe(topic);
                subscribedTopics.Add(topic);

                // TODO: Improve write performance here; don't loop all topics one by one
                this.SubscribedTopics = subscribedTopics.ToArray();
            }
            else
            {
                this.logger.LogInformation($"Subscribe: skipping topic \"{topic}\"; topic is already subscribed");
            }
        }

        /// <inheritdoc />
        public void UnsubscribeAllTopics()
        {
            foreach (var topic in this.SubscribedTopics)
            {
                Firebase.CloudMessaging.Messaging.SharedInstance.Unsubscribe(topic);
            }

            this.SubscribedTopics = null;
        }

        /// <inheritdoc />
        public void UnsubscribeTopics(string[] topics)
        {
            if (topics == null)
            {
                throw new ArgumentNullException(nameof(topics), $"Parameter '{nameof(topics)}' must not be null");
            }

            // TODO: Improve efficiency here (move to base class maybe)
            foreach (var t in topics)
            {
                this.UnsubscribeTopic(t);
            }
        }

        /// <inheritdoc />
        public void UnsubscribeTopic(string topic)
        {
            if (topic == null)
            {
                throw new ArgumentNullException(nameof(topic), "Topic must not be null");
            }

            if (topic == string.Empty)
            {
                throw new ArgumentException("Topic must not be empty", nameof(topic));
            }

            if (!this.hasToken)
            {
                this.pendingTopics.Enqueue(new Tuple<string, bool>(topic, false));
                return;
            }

            var subscribedTopics = new HashSet<string>(this.SubscribedTopics);
            if (subscribedTopics.Contains(topic))
            {
                this.logger.LogDebug($"Unsubscribe: topic=\"{topic}\"");

                Firebase.CloudMessaging.Messaging.SharedInstance.Unsubscribe(topic);
                subscribedTopics.Remove(topic);

                // TODO: Improve write performance here; don't loop all topics one by one
                this.SubscribedTopics = subscribedTopics.ToArray();
            }
            else
            {
                this.logger.LogInformation($"Unsubscribe: skipping topic \"{topic}\"; topic is not subscribed");
            }
        }

        private void DidReceiveNotificationResponse(UNUserNotificationCenter center, UNNotificationResponse response, Action completionHandler)
        {
            this.logger.LogDebug("DidReceiveNotificationResponse");

            var data = GetParameters(response.Notification.Request.Content.UserInfo);

            NotificationCategoryType notificationCategoryType;

            if (response.IsCustomAction)
            {
                notificationCategoryType = NotificationCategoryType.Custom;
            }
            else if (response.IsDismissAction)
            {
                notificationCategoryType = NotificationCategoryType.Dismiss;
            }
            else
            {
                notificationCategoryType = NotificationCategoryType.Default;
            }

            var actionIdentifier = $"{response.ActionIdentifier}";
            var identifier = actionIdentifier.Equals("com.apple.UNNotificationDefaultActionIdentifier", StringComparison.InvariantCultureIgnoreCase)
                ? null
                : actionIdentifier;

            if (string.IsNullOrEmpty(identifier))
            {
                this.HandleNotificationOpened(data, identifier, notificationCategoryType);
            }
            else
            {
                this.HandleNotificationAction(data, identifier, notificationCategoryType);
            }

            // Inform caller it has been handled
            completionHandler();
        }

        private void DidReceiveRegistrationToken(Firebase.CloudMessaging.Messaging messaging, string fcmToken)
        {
            this.logger.LogDebug("DidReceiveRegistrationToken");

            // Note that this callback will be fired everytime a new token is generated,
            // including the first time a token is received.

            if (!string.IsNullOrEmpty(fcmToken))
            {
                this.HandleTokenRefresh(fcmToken);

                this.hasToken = true;

                while (this.pendingTopics.TryDequeue(out var pendingTopic))
                {
                    if (pendingTopic != null)
                    {
                        if (pendingTopic.Item2)
                        {
                            this.SubscribeTopic(pendingTopic.Item1);
                        }
                        else
                        {
                            this.UnsubscribeTopic(pendingTopic.Item1);
                        }
                    }
                }
            }
        }

        /// <inheritdoc />
        protected override void ClearAllNotificationsPlatform()
        {
            // Remove all delivered notifications
            UNUserNotificationCenter.Current.RemoveAllDeliveredNotifications();
        }

        /// <inheritdoc />
        public void RemoveNotification(string tag, int id)
        {
            this.RemoveNotification(id);
        }

        /// <inheritdoc />
        public async void RemoveNotification(int id)
        {
            var NotificationIdKey = "id";
            if (UIDevice.CurrentDevice.CheckSystemVersion(10, 0))
            {
                var deliveredNotifications = await UNUserNotificationCenter.Current.GetDeliveredNotificationsAsync();
                var deliveredNotificationsMatches = deliveredNotifications.Where(u => $"{u.Request.Content.UserInfo[NotificationIdKey]}".Equals($"{id}")).Select(s => s.Request.Identifier).ToArray();
                if (deliveredNotificationsMatches.Length > 0)
                {
                    UNUserNotificationCenter.Current.RemoveDeliveredNotifications(deliveredNotificationsMatches);

                }
            }
            else
            {
                throw new NotSupportedException();
            }
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (!this.disposed)
            {
                if (disposing)
                {
                    if (UNUserNotificationCenter.Current.Delegate is UNUserNotificationCenterDelegateImpl)
                    {
                        UNUserNotificationCenter.Current.Delegate = null;
                    }

                    if (Firebase.CloudMessaging.Messaging.SharedInstance.Delegate is MessagingDelegateImpl)
                    {
                        Firebase.CloudMessaging.Messaging.SharedInstance.Delegate = null;
                    }
                }

                this.disposed = true;
            }
        }
    }
}
