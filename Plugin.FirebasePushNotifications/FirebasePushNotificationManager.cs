﻿#if IOS
using Foundation;
#endif

namespace Plugin.FirebasePushNotifications.Platforms
{
    // TODO: The situation here is not sorted out yet.
    // We have to make sure we have a platform implementation, as much code sharing as possible and working unit tests!
    public partial class FirebasePushNotificationManager : FirebasePushNotificationManagerBase
    {
    }
}
