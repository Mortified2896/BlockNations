#import <BackgroundTasks/BackgroundTasks.h>
#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import "UnityAppController.h"

void BNBackgroundExperimentScheduleLocalNotification(
    NSString *identifier,
    NSString *title,
    NSString *body,
    NSInteger badgeCount,
    void (^completion)(NSError *error));
void BNBackgroundExperimentRemovePendingNotifications(void);

static NSString * const BNBackgroundExperimentStateDefaultsKey = @"bn.pbp.backgroundExperiment.syncState";
static NSString * const BNBackgroundExperimentCursorDefaultsKey = @"bn.pbp.backgroundExperiment.lastNotifiedSeqByGameId";
static NSTimeInterval const BNBackgroundExperimentRefreshIntervalSeconds = 15.0 * 60.0;

static NSString * BNBackgroundExperimentTaskIdentifier(void)
{
    NSString *bundleIdentifier = [[NSBundle mainBundle] bundleIdentifier];
    if (bundleIdentifier.length <= 0)
    {
        bundleIdentifier = @"com.blocknations.app";
    }

    return [bundleIdentifier stringByAppendingString:@".pbp-refresh"];
}

static NSDictionary * BNBackgroundExperimentLoadState(void)
{
    NSString *json = [[NSUserDefaults standardUserDefaults] stringForKey:BNBackgroundExperimentStateDefaultsKey];
    if (json.length <= 0)
    {
        return nil;
    }

    NSData *data = [json dataUsingEncoding:NSUTF8StringEncoding];
    if (data == nil)
    {
        return nil;
    }

    id object = [NSJSONSerialization JSONObjectWithData:data options:0 error:nil];
    if (![object isKindOfClass:[NSDictionary class]])
    {
        return nil;
    }

    return (NSDictionary *)object;
}

static NSMutableDictionary<NSString *, NSNumber *> * BNBackgroundExperimentLoadCursorMap(void)
{
    NSDictionary *stored = [[NSUserDefaults standardUserDefaults] dictionaryForKey:BNBackgroundExperimentCursorDefaultsKey];
    NSMutableDictionary<NSString *, NSNumber *> *mutableMap = [NSMutableDictionary dictionary];
    if (![stored isKindOfClass:[NSDictionary class]])
    {
        return mutableMap;
    }

    for (NSString *key in stored)
    {
        id value = [stored objectForKey:key];
        if ([key isKindOfClass:[NSString class]] && [value isKindOfClass:[NSNumber class]])
        {
            [mutableMap setObject:value forKey:key];
        }
    }

    return mutableMap;
}

static void BNBackgroundExperimentSaveCursorMap(NSDictionary<NSString *, NSNumber *> *cursorMap)
{
    NSUserDefaults *defaults = [NSUserDefaults standardUserDefaults];
    if (cursorMap.count <= 0)
    {
        [defaults removeObjectForKey:BNBackgroundExperimentCursorDefaultsKey];
    }
    else
    {
        [defaults setObject:cursorMap forKey:BNBackgroundExperimentCursorDefaultsKey];
    }

    [defaults synchronize];
}

static void BNBackgroundExperimentSetBadgeCount(NSInteger count)
{
    NSInteger badgeCount = MAX((NSInteger)0, count);
    dispatch_async(dispatch_get_main_queue(), ^
    {
        [UIApplication sharedApplication].applicationIconBadgeNumber = badgeCount;
    });
}

static void BNBackgroundExperimentCancelScheduledRefresh(void)
{
    if (@available(iOS 13.0, *))
    {
        [[BGTaskScheduler sharedScheduler] cancelTaskRequestWithIdentifier:BNBackgroundExperimentTaskIdentifier()];
    }
}

static void BNBackgroundExperimentPruneCursorsToWatchedGames(NSArray *watchedGames)
{
    NSMutableSet<NSString *> *activeGameIds = [NSMutableSet set];
    for (id item in watchedGames)
    {
        if (![item isKindOfClass:[NSDictionary class]])
        {
            continue;
        }

        NSString *gameId = [(NSDictionary *)item objectForKey:@"gameId"];
        if (gameId.length > 0)
        {
            [activeGameIds addObject:gameId];
        }
    }

    NSMutableDictionary<NSString *, NSNumber *> *cursorMap = BNBackgroundExperimentLoadCursorMap();
    NSArray<NSString *> *existingKeys = [cursorMap allKeys];
    for (NSString *gameId in existingKeys)
    {
        if (![activeGameIds containsObject:gameId])
        {
            [cursorMap removeObjectForKey:gameId];
        }
    }

    BNBackgroundExperimentSaveCursorMap(cursorMap);
}

static void BNBackgroundExperimentScheduleNextRefresh(void)
{
    NSDictionary *state = BNBackgroundExperimentLoadState();
    BOOL enabled = [[state objectForKey:@"enabled"] boolValue];
    NSArray *watchedGames = [state objectForKey:@"watchedGames"];
    if (!enabled || ![watchedGames isKindOfClass:[NSArray class]] || watchedGames.count <= 0)
    {
        BNBackgroundExperimentCancelScheduledRefresh();
        return;
    }

    if (@available(iOS 13.0, *))
    {
        BGAppRefreshTaskRequest *request =
            [[BGAppRefreshTaskRequest alloc] initWithIdentifier:BNBackgroundExperimentTaskIdentifier()];
        request.earliestBeginDate = [NSDate dateWithTimeIntervalSinceNow:BNBackgroundExperimentRefreshIntervalSeconds];

        NSError *error = nil;
        [[BGTaskScheduler sharedScheduler] submitTaskRequest:request error:&error];
        if (error != nil)
        {
            NSLog(@"[BNBackgroundExperiment] Failed to submit app refresh task: %@", error);
        }
    }
}

static NSString * BNBackgroundExperimentNotificationTitleForGame(NSDictionary *game)
{
    NSString *displayName = [game objectForKey:@"displayName"];
    if (displayName.length > 0)
    {
        return displayName;
    }

    return @"Block Nations";
}

static NSString * BNBackgroundExperimentNotificationBodyForGame(NSDictionary *game)
{
    NSString *displayName = [game objectForKey:@"displayName"];
    if (displayName.length > 0)
    {
        return [NSString stringWithFormat:@"It's your turn in %@.", displayName];
    }

    return @"It's your turn in Block Nations.";
}

static NSDictionary * BNBackgroundExperimentBuildStatusItemMap(NSArray *items)
{
    NSMutableDictionary *itemMap = [NSMutableDictionary dictionary];
    for (id item in items)
    {
        if (![item isKindOfClass:[NSDictionary class]])
        {
            continue;
        }

        NSString *gameId = [(NSDictionary *)item objectForKey:@"gameId"];
        if (gameId.length > 0)
        {
            [itemMap setObject:item forKey:gameId];
        }
    }

    return itemMap;
}

static NSInteger BNBackgroundExperimentResolveBadgeCount(
    NSArray *watchedGames,
    NSDictionary *statusItemMap)
{
    NSInteger badgeCount = 0;
    for (id item in watchedGames)
    {
        if (![item isKindOfClass:[NSDictionary class]])
        {
            continue;
        }

        NSDictionary *game = (NSDictionary *)item;
        NSString *gameId = [game objectForKey:@"gameId"];
        if (gameId.length <= 0)
        {
            continue;
        }

        NSInteger localSeat = [[game objectForKey:@"localSeat"] integerValue];
        BOOL knownIsLocalTurn = [[game objectForKey:@"knownIsLocalTurn"] boolValue];
        BOOL isYourTurn = knownIsLocalTurn;

        NSDictionary *status = [statusItemMap objectForKey:gameId];
        if ([status isKindOfClass:[NSDictionary class]] &&
            [[status objectForKey:@"hasNewerThanKnown"] boolValue])
        {
            NSInteger turnSeat = [[status objectForKey:@"turnSeat"] integerValue];
            if (turnSeat == 0 || turnSeat == 1)
            {
                isYourTurn = localSeat == turnSeat;
            }
        }

        if (isYourTurn)
        {
            badgeCount++;
        }
    }

    return badgeCount;
}

static void BNBackgroundExperimentHandleRefreshTask(BGAppRefreshTask *task)
{
    BNBackgroundExperimentScheduleNextRefresh();

    NSDictionary *state = BNBackgroundExperimentLoadState();
    BOOL enabled = [[state objectForKey:@"enabled"] boolValue];
    NSString *baseUrl = [state objectForKey:@"baseUrl"];
    NSArray *watchedGames = [state objectForKey:@"watchedGames"];
    if (!enabled ||
        baseUrl.length <= 0 ||
        ![watchedGames isKindOfClass:[NSArray class]] ||
        watchedGames.count <= 0)
    {
        [task setTaskCompletedWithSuccess:YES];
        return;
    }

    NSMutableArray *requestGames = [NSMutableArray arrayWithCapacity:watchedGames.count];
    for (id item in watchedGames)
    {
        if (![item isKindOfClass:[NSDictionary class]])
        {
            continue;
        }

        NSDictionary *game = (NSDictionary *)item;
        NSString *gameId = [game objectForKey:@"gameId"];
        NSNumber *knownSeq = [game objectForKey:@"knownSeq"];
        if (gameId.length <= 0 || ![knownSeq isKindOfClass:[NSNumber class]])
        {
            continue;
        }

        [requestGames addObject:@{
            @"gameId": gameId,
            @"knownSeq": knownSeq
        }];
    }

    if (requestGames.count <= 0)
    {
        [task setTaskCompletedWithSuccess:YES];
        return;
    }

    NSURL *url = [NSURL URLWithString:[baseUrl stringByAppendingString:@"/pbp/turn/status"]];
    if (url == nil)
    {
        [task setTaskCompletedWithSuccess:NO];
        return;
    }

    NSDictionary *bodyObject = @{ @"games": requestGames };
    NSData *bodyData = [NSJSONSerialization dataWithJSONObject:bodyObject options:0 error:nil];
    if (bodyData == nil)
    {
        [task setTaskCompletedWithSuccess:NO];
        return;
    }

    NSMutableURLRequest *request = [NSMutableURLRequest requestWithURL:url];
    request.HTTPMethod = @"POST";
    request.HTTPBody = bodyData;
    [request setValue:@"application/json" forHTTPHeaderField:@"Content-Type"];

    NSString *apiKey = [state objectForKey:@"apiKey"];
    if (apiKey.length > 0)
    {
        [request setValue:apiKey forHTTPHeaderField:@"X-BlockNations-Api-Key"];
    }

    NSURLSessionConfiguration *configuration = [NSURLSessionConfiguration ephemeralSessionConfiguration];
    configuration.timeoutIntervalForRequest = 10.0;
    configuration.timeoutIntervalForResource = 15.0;
    NSURLSession *session = [NSURLSession sessionWithConfiguration:configuration];

    __block NSURLSessionDataTask *dataTask = nil;
    __block BOOL taskCompleted = NO;
    void (^completeTask)(BOOL) = ^(BOOL wasSuccessful)
    {
        @synchronized (task)
        {
            if (taskCompleted)
            {
                return;
            }

            taskCompleted = YES;
        }

        [task setTaskCompletedWithSuccess:wasSuccessful];
    };
    task.expirationHandler = ^
    {
        [dataTask cancel];
        [session invalidateAndCancel];
        completeTask(NO);
    };

    dataTask = [session dataTaskWithRequest:request
                          completionHandler:^(NSData * _Nullable data,
                                              NSURLResponse * _Nullable response,
                                              NSError * _Nullable error)
    {
        BOOL success = NO;

        if (error == nil &&
            [response isKindOfClass:[NSHTTPURLResponse class]] &&
            ((NSHTTPURLResponse *)response).statusCode == 200 &&
            data.length > 0)
        {
            id jsonObject = [NSJSONSerialization JSONObjectWithData:data options:0 error:nil];
            NSDictionary *json = [jsonObject isKindOfClass:[NSDictionary class]] ? (NSDictionary *)jsonObject : nil;
            NSArray *items = [json objectForKey:@"games"];
            if ([[json objectForKey:@"ok"] boolValue] && [items isKindOfClass:[NSArray class]])
            {
                NSDictionary *statusItemMap = BNBackgroundExperimentBuildStatusItemMap(items);
                NSInteger badgeCount = BNBackgroundExperimentResolveBadgeCount(watchedGames, statusItemMap);
                BNBackgroundExperimentSetBadgeCount(badgeCount);

                NSMutableDictionary<NSString *, NSNumber *> *cursorMap = BNBackgroundExperimentLoadCursorMap();
                dispatch_group_t notificationGroup = dispatch_group_create();
                __block BOOL hadNotificationError = NO;

                for (id watchedItem in watchedGames)
                {
                    if (![watchedItem isKindOfClass:[NSDictionary class]])
                    {
                        continue;
                    }

                    NSDictionary *game = (NSDictionary *)watchedItem;
                    NSString *gameId = [game objectForKey:@"gameId"];
                    NSDictionary *status = [statusItemMap objectForKey:gameId];
                    if (![status isKindOfClass:[NSDictionary class]])
                    {
                        continue;
                    }

                    if (![[status objectForKey:@"hasNewerThanKnown"] boolValue])
                    {
                        continue;
                    }

                    NSInteger localSeat = [[game objectForKey:@"localSeat"] integerValue];
                    NSInteger turnSeat = [[status objectForKey:@"turnSeat"] integerValue];
                    if (turnSeat != localSeat)
                    {
                        continue;
                    }

                    NSInteger candidateSeq = [[status objectForKey:@"latestSeq"] integerValue];
                    if (candidateSeq <= 0)
                    {
                        candidateSeq = [[status objectForKey:@"nextSeqAfterKnown"] integerValue];
                    }

                    NSInteger lastNotifiedSeq = [[cursorMap objectForKey:gameId] integerValue];
                    if (candidateSeq <= lastNotifiedSeq)
                    {
                        continue;
                    }

                    [cursorMap setObject:@(candidateSeq) forKey:gameId];

                    NSString *identifier = [NSString stringWithFormat:@"bn.pbp.%@.%ld", gameId, (long)candidateSeq];
                    NSString *title = BNBackgroundExperimentNotificationTitleForGame(game);
                    NSString *body = BNBackgroundExperimentNotificationBodyForGame(game);

                    dispatch_group_enter(notificationGroup);
                    BNBackgroundExperimentScheduleLocalNotification(
                        identifier,
                        title,
                        body,
                        badgeCount,
                        ^(NSError *notificationError)
                    {
                        if (notificationError != nil)
                        {
                            hadNotificationError = YES;
                            NSLog(@"[BNBackgroundExperiment] Failed to schedule local notification: %@", notificationError);
                        }

                        dispatch_group_leave(notificationGroup);
                    });
                }

                BNBackgroundExperimentSaveCursorMap(cursorMap);

                dispatch_group_notify(notificationGroup, dispatch_get_global_queue(QOS_CLASS_UTILITY, 0), ^
                {
                    [session finishTasksAndInvalidate];
                    completeTask(!hadNotificationError);
                });

                success = YES;
            }
        }

        if (!success)
        {
            [session finishTasksAndInvalidate];
            completeTask(NO);
        }
    }];

    [dataTask resume];
}

@interface BlockNationsBackgroundRefreshAppController : UnityAppController
@end

@implementation BlockNationsBackgroundRefreshAppController

- (BOOL)application:(UIApplication *)application didFinishLaunchingWithOptions:(NSDictionary *)launchOptions
{
    if (@available(iOS 13.0, *))
    {
        NSString *identifier = BNBackgroundExperimentTaskIdentifier();
        [[BGTaskScheduler sharedScheduler] registerForTaskWithIdentifier:identifier
                                                              usingQueue:nil
                                                           launchHandler:^(__kindof BGTask *task)
        {
            if ([task isKindOfClass:[BGAppRefreshTask class]])
            {
                BNBackgroundExperimentHandleRefreshTask((BGAppRefreshTask *)task);
            }
            else
            {
                [task setTaskCompletedWithSuccess:NO];
            }
        }];
    }

    return [super application:application didFinishLaunchingWithOptions:launchOptions];
}

@end

IMPL_APP_CONTROLLER_SUBCLASS(BlockNationsBackgroundRefreshAppController)

extern "C" void BNBackgroundExperimentSyncState(const char *json)
{
    NSString *jsonString = json != nullptr ? [NSString stringWithUTF8String:json] : nil;
    NSUserDefaults *defaults = [NSUserDefaults standardUserDefaults];

    if (jsonString.length <= 0)
    {
        [defaults removeObjectForKey:BNBackgroundExperimentStateDefaultsKey];
        [defaults synchronize];
        BNBackgroundExperimentCancelScheduledRefresh();
        BNBackgroundExperimentRemovePendingNotifications();
        BNBackgroundExperimentSaveCursorMap(@{});
        return;
    }

    NSData *data = [jsonString dataUsingEncoding:NSUTF8StringEncoding];
    id parsed = data != nil ? [NSJSONSerialization JSONObjectWithData:data options:0 error:nil] : nil;
    NSDictionary *state = [parsed isKindOfClass:[NSDictionary class]] ? (NSDictionary *)parsed : nil;
    if (state == nil)
    {
        return;
    }

    [defaults setObject:jsonString forKey:BNBackgroundExperimentStateDefaultsKey];
    [defaults synchronize];

    BOOL enabled = [[state objectForKey:@"enabled"] boolValue];
    NSArray *watchedGames = [state objectForKey:@"watchedGames"];
    if (!enabled || ![watchedGames isKindOfClass:[NSArray class]] || watchedGames.count <= 0)
    {
        BNBackgroundExperimentCancelScheduledRefresh();
        BNBackgroundExperimentRemovePendingNotifications();
        BNBackgroundExperimentSaveCursorMap(@{});
        return;
    }

    BNBackgroundExperimentPruneCursorsToWatchedGames(watchedGames);
    BNBackgroundExperimentScheduleNextRefresh();
}

extern "C" void BNBackgroundExperimentRemoveGame(const char *gameId)
{
    NSString *gameIdString = gameId != nullptr ? [NSString stringWithUTF8String:gameId] : nil;
    if (gameIdString.length <= 0)
    {
        return;
    }

    NSDictionary *state = BNBackgroundExperimentLoadState();
    if (state == nil)
    {
        return;
    }

    NSArray *watchedGames = [state objectForKey:@"watchedGames"];
    if (![watchedGames isKindOfClass:[NSArray class]] || watchedGames.count <= 0)
    {
        return;
    }

    NSMutableArray *remainingGames = [NSMutableArray array];
    for (id item in watchedGames)
    {
        if (![item isKindOfClass:[NSDictionary class]])
        {
            continue;
        }

        NSDictionary *game = (NSDictionary *)item;
        NSString *existingGameId = [game objectForKey:@"gameId"];
        if (![existingGameId isEqualToString:gameIdString])
        {
            [remainingGames addObject:game];
        }
    }

    NSMutableDictionary *mutableState = [state mutableCopy];
    [mutableState setObject:remainingGames forKey:@"watchedGames"];
    NSData *data = [NSJSONSerialization dataWithJSONObject:mutableState options:0 error:nil];
    if (data != nil)
    {
        NSString *jsonString = [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
        if (jsonString.length > 0)
        {
            [[NSUserDefaults standardUserDefaults] setObject:jsonString forKey:BNBackgroundExperimentStateDefaultsKey];
            [[NSUserDefaults standardUserDefaults] synchronize];
        }
    }

    NSMutableDictionary<NSString *, NSNumber *> *cursorMap = BNBackgroundExperimentLoadCursorMap();
    [cursorMap removeObjectForKey:gameIdString];
    BNBackgroundExperimentSaveCursorMap(cursorMap);

    if (remainingGames.count <= 0)
    {
        BNBackgroundExperimentCancelScheduledRefresh();
        BNBackgroundExperimentRemovePendingNotifications();
    }
}
