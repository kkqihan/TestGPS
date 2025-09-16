using System;
using System.Collections;
using UnityEngine;
using Newtonsoft.Json;

namespace Platform
{
    /// <summary>
    /// 提供：
    /// 1. 启动定位并获取经纬度
    /// 2. 查询最近一次定位数据
    /// 3. 反向地理编码（经纬度 -> 地址）
    /// 说明：
    /// - 使用 Unity 内置 LocationService 获取坐标
    /// - 反向地理编码需依赖原生实现：
    ///   * Android：通过自定义 UnityPlayerActivity 扩展内调用 android.location.Geocoder
    ///   * iOS：通过 CLGeocoder，在 .mm 中实现 ReverseGeocodeNative 并暴露 __Internal 符号
    /// - 若原生层暂未实现，ReverseGeocode 将回调失败
    /// </summary>
    public class LocationApi : MonoBehaviour
    {
        #region Singleton
        private static LocationApi _instance;
        public static LocationApi Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("LocationApi");
                    _instance = go.AddComponent<LocationApi>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }
        #endregion

        #region StateFields
        public bool IsInitialized { get; private set; }
        private float _desiredAccuracy = 10f; // meters
        private float _updateDistance = 5f;   // meters
        public event Action<GeoAddress> OnGetAddressSuccess;
        public event Action<string> OnGetAddressError;
        #endregion

#if UNITY_IOS && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void ReverseGeocodeNative(double lat, double lon, string gameObjectName, string successCallback, string failCallback);
#endif

        // Android 原生：在自定义 Activity 中实现 public void reverseGeocode(double lat, double lon, String gameObjectName, String successCallback, String failCallback)
        // 通过 UnitySendMessage 回调

        #region PublicAPI
        public void Initialize(float desiredAccuracyMeters = 10f, float updateDistanceMeters = 5f)
        {
            if (IsInitialized) return;
            _desiredAccuracy = desiredAccuracyMeters;
            _updateDistance = updateDistanceMeters;
            IsInitialized = true;
        }

        /// <summary>
        /// 刷新一次位置信息：启动定位 -> 获取坐标 -> 反向地理编码 -> 缓存当前地址 -> 停止定位
        /// 回调：success, message("ok" / 错误原因)
        /// </summary>
        public void RefreshAddress(int timeoutSeconds = 20)
        {
            if (_refreshInProgress) { OnGetAddressError?.Invoke("refresh_in_progress"); return; }
            if (!IsInitialized) Initialize();
            if (_refreshRoutine != null) StopCoroutine(_refreshRoutine);
            _refreshRoutine = StartCoroutine(CoRefreshAddress(timeoutSeconds));
        }

        public bool TryGetCurrentAddress(out GeoAddress addr)
        {
            addr = _currentAddress;
            return _hasCurrentAddress && addr != null;
        }
        #endregion

        // 显式请求 Android 权限（在调用 StartLocation 前可选调用）
        #region Permissions
        public void RequestAndroidPermissions(Action<bool> callback = null)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            StartCoroutine(CoRequestAndroidPermissions(callback));
#else
            callback?.Invoke(true);
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
    private IEnumerator CoRequestAndroidPermissions(Action<bool> callback)
        {
            string fine = "android.permission.ACCESS_FINE_LOCATION";
            string coarse = "android.permission.ACCESS_COARSE_LOCATION";
            bool grantedFine = UnityEngine.Android.Permission.HasUserAuthorizedPermission(fine);
            bool grantedCoarse = UnityEngine.Android.Permission.HasUserAuthorizedPermission(coarse);
            if (!grantedFine) UnityEngine.Android.Permission.RequestUserPermission(fine);
            if (!grantedCoarse) UnityEngine.Android.Permission.RequestUserPermission(coarse);
            // 等一帧再检查
            yield return null;
            grantedFine = UnityEngine.Android.Permission.HasUserAuthorizedPermission(fine);
            grantedCoarse = UnityEngine.Android.Permission.HasUserAuthorizedPermission(coarse);
            callback?.Invoke(grantedFine || grantedCoarse);
        }
#endif

        #endregion


        #region CurrentAddress
        private GeoAddress _currentAddress;
        private bool _hasCurrentAddress;
        private LocationInfo _currLocationInfo;
        private bool _refreshInProgress;
        private Coroutine _refreshRoutine;

        public bool HasCurrentAddress => _hasCurrentAddress;


        private IEnumerator CoRefreshAddress(int timeoutSeconds)
        {
            // 防止并发
            _refreshInProgress = true;

            // 启动定位（一次性）
            Input.location.Start(_desiredAccuracy, _updateDistance);
            float start = Time.realtimeSinceStartup;
            while (Input.location.status == LocationServiceStatus.Initializing && (Time.realtimeSinceStartup - start) < timeoutSeconds)
            {
                yield return null;
            }
            if (Input.location.status != LocationServiceStatus.Running)
            {
                string err = "start_fail:" + Input.location.status;
                OnGetAddressError?.Invoke(err);
                _refreshInProgress = false;
                yield break;
            }

            // 更新当前的定位数据
            _currLocationInfo = Input.location.lastData;

            // 反向地理编码
            bool done = false; bool okAddr = false; GeoAddress gotAddr = null;
            ReverseGeocodeAddress(_currLocationInfo.latitude, _currLocationInfo.longitude, (ok, addr) => { okAddr = ok; gotAddr = addr; done = true; });
            while (!done && (Time.realtimeSinceStartup - start) < timeoutSeconds)
            {
                yield return null;
            }
            if (!done)
            {
                OnGetAddressError?.Invoke("reverse_timeout");
            }
            else if (!okAddr)
            {
                OnGetAddressError?.Invoke("reverse_fail");
            }
            else
            {
                _currentAddress = gotAddr;
                _hasCurrentAddress = true;
                OnGetAddressSuccess?.Invoke(gotAddr);
            }
            Input.location.Stop();
            _refreshInProgress = false;
            _refreshRoutine = null;
        }


        private void ReverseGeocode(double latitude, double longitude, Action<bool, string> callback)
        {
            // 缓存一下回调，供原生调用时使用
            _pendingReverseCallback = callback;

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var jc = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = jc.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    activity.Call("reverseGeocode", latitude, longitude, gameObject.name, nameof(OnReverseGeocodeSuccess), nameof(OnReverseGeocodeFail));
                }
            }
            catch (Exception e)
            {
                _pendingReverseCallback?.Invoke(false, "Android reverseGeocode exception: " + e.Message);
            }
#elif UNITY_IOS && !UNITY_EDITOR
            try
            {
                ReverseGeocodeNative(latitude, longitude, gameObject.name, nameof(OnReverseGeocodeSuccess), nameof(OnReverseGeocodeFail));
            }
            catch (Exception e)
            {
                _pendingReverseCallback?.Invoke(false, "iOS reverseGeocode exception: " + e.Message);
            }
#else
            _pendingReverseCallback?.Invoke(false, "ReverseGeocode not supported in Editor or this platform");
#endif
        }

        private Action<bool, string> _pendingReverseCallback;

        // 原生通过 UnitySendMessage(gameObjectName, successCallback, jsonAddressString)
        private void OnReverseGeocodeSuccess(string addressJson)
        {
            var cb = _pendingReverseCallback; // 捕获后清空，避免多次调用
            _pendingReverseCallback = null;
            RunOnMainThread(() => cb?.Invoke(true, addressJson));
        }

        private void OnReverseGeocodeFail(string error)
        {
            var cb = _pendingReverseCallback;
            _pendingReverseCallback = null;
            RunOnMainThread(() => cb?.Invoke(false, error));
        }
        #endregion // CurrentAddress

        // 主线程事件分发封装
        #region MainThreadDispatch
        private static readonly object _dispatchLock = new object();
        private static readonly System.Collections.Generic.List<Action> _dispatchList = new System.Collections.Generic.List<Action>();
        private static bool _forceMainThreadCallbacks = true; // 可配置，默认强制

        public void SetForceMainThreadCallbacks(bool enable) => _forceMainThreadCallbacks = enable;

        private static void RunOnMainThread(Action a)
        {
            if (a == null) return;
            if (!_forceMainThreadCallbacks)
            {
                a();
                return;
            }
            lock (_dispatchLock)
            {
                _dispatchList.Add(a);
            }
        }

        private void Update()
        {
            if (_dispatchList.Count == 0) return;
            Action[] snapshot;
            lock (_dispatchLock)
            {
                if (_dispatchList.Count == 0) return;
                snapshot = _dispatchList.ToArray();
                _dispatchList.Clear();
            }
            foreach (var act in snapshot)
            {
                try { act?.Invoke(); } catch (Exception e) { Debug.LogError("MainThread dispatch exception: " + e); }
            }
        }
        #endregion // MainThreadDispatch

        #region DataModels
        [Serializable]
        public class GeoAddress
        {
            public string country;
            public string isoCountryCode;
            public string admin;          // 州/省
            public string locality;       // 市
            public string subLocality;    // 区/县
            public string thoroughfare;   // 街道
            public string subThoroughfare;// 门牌/街道号
            public string feature;        // 地标(安卓 featureName) 或 name(iOS)
            public string postalCode;
            public string fullLine;       // 原始第一行（Android AddressLine(0) 或组合）
            public string formatted;      // 组合格式化

            public override string ToString() => formatted ?? fullLine ?? feature ?? ($"{locality} {thoroughfare}");
        }

        public bool TryParseGeoAddress(string json, out GeoAddress addr)
        {
            addr = null;
            if (string.IsNullOrEmpty(json)) return false;
            json = json.Replace("/*cached*/", string.Empty);
            try
            {
                addr = JsonConvert.DeserializeObject<GeoAddress>(json);
                if (addr == null) return false;
                // 兼容 iOS 旧字段 name -> feature
                if (string.IsNullOrEmpty(addr.feature))
                {
                    // 如果 JSON 中可能存在 name 字段，但未绑定，则尝试动态解析
                    try
                    {
                        var dyn = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(json);
                        var nameToken = dyn?["name"];
                        if (nameToken != null && string.IsNullOrEmpty(addr.feature))
                            addr.feature = nameToken.ToString();
                    }
                    catch { /* 忽略二次解析失败 */ }
                }
                if (string.IsNullOrEmpty(addr.fullLine))
                {
                    addr.fullLine = ComposeLine(addr);
                }
                if (string.IsNullOrEmpty(addr.formatted))
                {
                    addr.formatted = ComposeFormatted(addr);
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("GeoAddress Newtonsoft 解析失败: " + e.Message + " raw:" + json);
                return false;
            }
        }

        public void ReverseGeocodeAddress(double latitude, double longitude, Action<bool, GeoAddress> callback)
        {
            ReverseGeocode(latitude, longitude, (ok, json) =>
            {
                if (!ok)
                {
                    callback?.Invoke(false, null);
                    return;
                }
                if (TryParseGeoAddress(json, out var addr))
                {
                    callback?.Invoke(true, addr);
                }
                else
                {
                    callback?.Invoke(false, null);
                }
            });
        }

        private static string ComposeLine(GeoAddress a)
        {
            return string.Join(", ", new[] { a.feature, a.subThoroughfare, a.thoroughfare, a.subLocality, a.locality, a.admin, a.country });
        }
        private static string ComposeFormatted(GeoAddress a)
        {
            return string.Join(" ", new[] { a.country, a.admin, a.locality, a.subLocality, a.thoroughfare, a.subThoroughfare, a.feature }).Trim();
        }
        #endregion

        #region Lifecycle
        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }
        #endregion
    }
}
