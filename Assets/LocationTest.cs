using UnityEngine;
using UnityEngine.UI;
using Platform;

public class LocationTest : MonoBehaviour
{
    Button btn_StartLocation;

    Button btn_StopLocation;


    public void Start()
    {
        LocationApi.Instance.Initialize();

        //监听事件
        LocationApi.Instance.OnGetAddressSuccess += OnGetAddressSuccess;
        LocationApi.Instance.OnGetAddressError += OnGetAddreessError;
    }


    #region 按钮响应
    public void OnClickBtn_RefreshAddress()
    {
        LocationApi.Instance.RefreshAddress();
    }

    public void OnClickBtn_GetCurrentAddress()
    {
        if (LocationApi.Instance.TryGetCurrentAddress(out LocationApi.GeoAddress address))
        {
            Debug.Log($"获取位置信息成功: json:{JsonUtility.ToJson(address)}");
        }
        else
        {
            Debug.Log($"获取位置信息失败");
        }
    }
    #endregion

    #region  事件响应
    private void OnGetAddressSuccess(LocationApi.GeoAddress address)
    {
        Debug.Log($"定位更新: json:{JsonUtility.ToJson(address)}");
    }
    private void OnGetAddreessError(string error)
    {
        Debug.Log($"定位错误: {error}");
    }
    #endregion
}