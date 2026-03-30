#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <UserNotifications/UserNotifications.h>

void BNBackgroundExperimentScheduleLocalNotification(
    NSString *identifier,
    NSString *title,
    NSString *body,
    NSInteger badgeCount,
    void (^completion)(NSError *error));

extern "C" void BNRequestBackgroundExperimentNotificationAuthorization(void)
{
    void (^requestAuthorization)(void) = ^
    {
        UNUserNotificationCenter *center = [UNUserNotificationCenter currentNotificationCenter];
        UNAuthorizationOptions options = UNAuthorizationOptionAlert | UNAuthorizationOptionBadge;
        [center requestAuthorizationWithOptions:options
                              completionHandler:^(BOOL granted, NSError * _Nullable error)
        {
            if (error != nil)
            {
                NSLog(@"[iOS Debug Notification] Authorization request failed: %@.", error.localizedDescription);
                return;
            }

            NSLog(@"[iOS Debug Notification] Authorization request completed. granted=%@.", granted ? @"YES" : @"NO");
        }];
    };

    if ([NSThread isMainThread])
    {
        requestAuthorization();
    }
    else
    {
        dispatch_async(dispatch_get_main_queue(), requestAuthorization);
    }
}

extern "C" void BNTriggerDebugLocalNotification(void)
{
    NSLog(@"[iOS Debug Notification] BNTriggerDebugLocalNotification entered.");
    UNUserNotificationCenter *center = [UNUserNotificationCenter currentNotificationCenter];
    [center getNotificationSettingsWithCompletionHandler:^(UNNotificationSettings * _Nonnull settings)
    {
        NSString *authorizationStatus = @"unknown";
        switch (settings.authorizationStatus)
        {
            case UNAuthorizationStatusNotDetermined:
                authorizationStatus = @"notDetermined";
                break;
            case UNAuthorizationStatusDenied:
                authorizationStatus = @"denied";
                break;
            case UNAuthorizationStatusAuthorized:
                authorizationStatus = @"authorized";
                break;
#if __IPHONE_OS_VERSION_MAX_ALLOWED >= 120000
            case UNAuthorizationStatusProvisional:
                authorizationStatus = @"provisional";
                break;
#endif
#if __IPHONE_OS_VERSION_MAX_ALLOWED >= 140000
            case UNAuthorizationStatusEphemeral:
                authorizationStatus = @"ephemeral";
                break;
#endif
        }

        NSLog(@"[iOS Debug Notification] Current authorization status: %@.", authorizationStatus);
    }];

    BNRequestBackgroundExperimentNotificationAuthorization();
    BNBackgroundExperimentScheduleLocalNotification(
        @"bn.pbp.debug.local-notification",
        @"Block Nations",
        @"It’s your turn in Block Nations!",
        [UIApplication sharedApplication].applicationIconBadgeNumber,
        ^(NSError *error)
    {
        if (error != nil)
        {
            NSLog(@"[iOS Debug Notification] Local notification scheduling failed: %@.", error.localizedDescription);
            return;
        }

        NSLog(@"[iOS Debug Notification] Local notification scheduling succeeded.");
    });
}

void BNBackgroundExperimentRemovePendingNotifications(void)
{
    void (^removeNotifications)(void) = ^
    {
        UNUserNotificationCenter *center = [UNUserNotificationCenter currentNotificationCenter];
        [center removePendingNotificationRequestsWithIdentifiers:@[@"bn.pbp.summary"]];
    };

    if ([NSThread isMainThread])
    {
        removeNotifications();
    }
    else
    {
        dispatch_async(dispatch_get_main_queue(), removeNotifications);
    }
}

void BNBackgroundExperimentScheduleLocalNotification(
    NSString *identifier,
    NSString *title,
    NSString *body,
    NSInteger badgeCount,
    void (^completion)(NSError *error))
{
    void (^scheduleNotification)(void) = ^
    {
        UNMutableNotificationContent *content = [[UNMutableNotificationContent alloc] init];
        content.title = title.length > 0 ? title : @"Block Nations";
        content.body = body.length > 0 ? body : @"It's your turn in Block Nations.";
        content.badge = [NSNumber numberWithInteger:MAX((NSInteger)0, badgeCount)];

        UNTimeIntervalNotificationTrigger *trigger =
            [UNTimeIntervalNotificationTrigger triggerWithTimeInterval:1 repeats:NO];
        UNNotificationRequest *request =
            [UNNotificationRequest requestWithIdentifier:identifier
                                                 content:content
                                                 trigger:trigger];

        UNUserNotificationCenter *center = [UNUserNotificationCenter currentNotificationCenter];
        [center addNotificationRequest:request withCompletionHandler:^(NSError * _Nullable error)
        {
            if (completion != nil)
            {
                completion(error);
            }
        }];
    };

    if ([NSThread isMainThread])
    {
        scheduleNotification();
    }
    else
    {
        dispatch_async(dispatch_get_main_queue(), scheduleNotification);
    }
}
