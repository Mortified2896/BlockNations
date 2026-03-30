#import <UserNotifications/UserNotifications.h>

extern "C" void BNRequestBadgeAuthorization(void)
{
    void (^requestBadgeAuthorization)(void) = ^
    {
        UNUserNotificationCenter *center = [UNUserNotificationCenter currentNotificationCenter];
        UNAuthorizationOptions options = UNAuthorizationOptionBadge;

        [center requestAuthorizationWithOptions:options
                              completionHandler:^(__unused BOOL granted, __unused NSError * _Nullable error)
        {
        }];
    };

    if ([NSThread isMainThread])
    {
        requestBadgeAuthorization();
    }
    else
    {
        dispatch_async(dispatch_get_main_queue(), requestBadgeAuthorization);
    }
}
