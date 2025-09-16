#import <CoreLocation/CoreLocation.h>
#if defined(__cplusplus)
extern "C" {
#endif
    void UnitySendMessage(const char* obj, const char* method, const char* msg);
#if defined(__cplusplus)
}
#endif

@interface BriazeReverseGeoHelper : NSObject
@property(nonatomic,strong) CLGeocoder *geocoder;
+ (instancetype)shared;
@end

@implementation BriazeReverseGeoHelper
+ (instancetype)shared { static BriazeReverseGeoHelper *h; static dispatch_once_t onceToken; dispatch_once(&onceToken, ^{ h=[BriazeReverseGeoHelper new]; h.geocoder=[CLGeocoder new]; }); return h; }
@end

static NSString* SafeStr(NSString* s){ return s ? s : @""; }
static NSString* BuildFullLine(CLPlacemark* pm){
    NSArray* parts = @[ SafeStr(pm.name), SafeStr(pm.subThoroughfare), SafeStr(pm.thoroughfare), SafeStr(pm.subLocality), SafeStr(pm.locality), SafeStr(pm.administrativeArea), SafeStr(pm.country) ];
    NSMutableArray *filled = [NSMutableArray array];
    for(NSString* p in parts){ if(p.length>0) [filled addObject:p]; }
    return [filled componentsJoinedByString:@", "];
}
static NSString* BuildFormatted(CLPlacemark* pm){
    NSArray* parts = @[ SafeStr(pm.country), SafeStr(pm.administrativeArea), SafeStr(pm.locality), SafeStr(pm.subLocality), SafeStr(pm.thoroughfare), SafeStr(pm.subThoroughfare), SafeStr(pm.name) ];
    NSMutableArray *filled = [NSMutableArray array];
    for(NSString* p in parts){ if(p.length>0) [filled addObject:p]; }
    return [filled componentsJoinedByString:@" "];
}

extern "C" void ReverseGeocodeNative(double lat, double lon, const char* goNameC, const char* successC, const char* failC){
    NSString *goName = [NSString stringWithUTF8String:goNameC];
    NSString *success = [NSString stringWithUTF8String:successC];
    NSString *fail = [NSString stringWithUTF8String:failC];
    CLLocation *loc = [[CLLocation alloc] initWithLatitude:lat longitude:lon];
    [[BriazeReverseGeoHelper shared].geocoder reverseGeocodeLocation:loc completionHandler:^(NSArray<CLPlacemark *> * _Nullable placemarks, NSError * _Nullable error) {
        if(error || placemarks.count==0){
            UnitySendMessage(goName.UTF8String, fail.UTF8String, error ? error.localizedDescription.UTF8String : "no_result");
            return;
        }
        CLPlacemark *pm = placemarks.firstObject;
        NSString *fullLine = BuildFullLine(pm);
        NSDictionary *dict = @{ @"country": SafeStr(pm.country),
                                 @"isoCountryCode": SafeStr(pm.ISOcountryCode),
                                 @"admin": SafeStr(pm.administrativeArea),
                                 @"locality": SafeStr(pm.locality),
                                 @"subLocality": SafeStr(pm.subLocality),
                                 @"thoroughfare": SafeStr(pm.thoroughfare),
                                 @"subThoroughfare": SafeStr(pm.subThoroughfare),
                                 @"feature": SafeStr(pm.name),
                                 @"postalCode": SafeStr(pm.postalCode),
                                 @"fullLine": fullLine,
                                 @"formatted": BuildFormatted(pm)
        };
        NSData *data = [NSJSONSerialization dataWithJSONObject:dict options:0 error:nil];
        NSString *json = [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
        UnitySendMessage(goName.UTF8String, success.UTF8String, json.UTF8String);
    }];
}
