#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>

static BOOL BNReminderShareSheetVisible = NO;

static UIViewController *BNTopViewController(void)
{
    UIWindow *keyWindow = nil;

    if (@available(iOS 13.0, *))
    {
        for (UIScene *scene in [UIApplication sharedApplication].connectedScenes)
        {
            if (![scene isKindOfClass:[UIWindowScene class]])
            {
                continue;
            }

            UIWindowScene *windowScene = (UIWindowScene *)scene;
            if (windowScene.activationState != UISceneActivationStateForegroundActive)
            {
                continue;
            }

            for (UIWindow *window in windowScene.windows)
            {
                if (window.isKeyWindow)
                {
                    keyWindow = window;
                    break;
                }
            }

            if (keyWindow != nil)
            {
                break;
            }
        }
    }

    if (keyWindow == nil)
    {
        keyWindow = [UIApplication sharedApplication].keyWindow;
    }

    UIViewController *controller = keyWindow.rootViewController;
    while (controller.presentedViewController != nil)
    {
        controller = controller.presentedViewController;
    }

    return controller;
}

extern "C" bool BNPresentReminderShareSheet(const char *text)
{
    if (text == NULL || BNReminderShareSheetVisible)
    {
        return false;
    }

    NSString *message = [NSString stringWithUTF8String:text];
    if (message == nil || message.length == 0)
    {
        return false;
    }

    __block BOOL didSchedulePresentation = NO;
    void (^presentShareSheet)(void) = ^
    {
        UIViewController *controller = BNTopViewController();
        if (controller == nil)
        {
            return;
        }

        BNReminderShareSheetVisible = YES;
        didSchedulePresentation = YES;

        UIActivityViewController *shareController =
            [[UIActivityViewController alloc] initWithActivityItems:@[message] applicationActivities:nil];

        shareController.completionWithItemsHandler =
            ^(UIActivityType _Nullable activityType,
              BOOL completed,
              NSArray *_Nullable returnedItems,
              NSError *_Nullable activityError)
        {
            BNReminderShareSheetVisible = NO;
        };

        if (UI_USER_INTERFACE_IDIOM() == UIUserInterfaceIdiomPad)
        {
            UIPopoverPresentationController *popover = shareController.popoverPresentationController;
            if (popover != nil)
            {
                popover.sourceView = controller.view;
                popover.sourceRect = controller.view.bounds;
            }
        }

        [controller presentViewController:shareController animated:YES completion:nil];
    };

    if ([NSThread isMainThread])
    {
        presentShareSheet();
    }
    else
    {
        dispatch_sync(dispatch_get_main_queue(), presentShareSheet);
    }

    return didSchedulePresentation;
}
