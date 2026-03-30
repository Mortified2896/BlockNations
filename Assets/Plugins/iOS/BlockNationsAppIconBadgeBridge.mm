#import <UIKit/UIKit.h>

extern "C" void BNSetApplicationIconBadgeNumber(int count)
{
    NSInteger badgeCount = count < 0 ? 0 : count;

    void (^applyBadge)(void) = ^
    {
        [UIApplication sharedApplication].applicationIconBadgeNumber = badgeCount;
    };

    if ([NSThread isMainThread])
    {
        applyBadge();
    }
    else
    {
        dispatch_sync(dispatch_get_main_queue(), applyBadge);
    }
}
