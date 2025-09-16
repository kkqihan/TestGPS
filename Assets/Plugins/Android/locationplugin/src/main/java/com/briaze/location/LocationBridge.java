package com.briaze.location;

import android.location.Address;
import android.location.Geocoder;
import android.os.Handler;
import android.os.Looper;

import com.unity3d.player.UnityPlayer;

import org.json.JSONObject;

import java.util.List;
import java.util.Locale;

public class LocationBridge {

    private static Handler mainHandler = new Handler(Looper.getMainLooper());

    public static void reverseGeocode(final double lat, final double lon, final String goName, final String successCb, final String failCb){
        new Thread(() -> {
            try {
                Geocoder geocoder = new Geocoder(UnityPlayer.currentActivity, Locale.getDefault());
                List<Address> list = geocoder.getFromLocation(lat, lon, 1);
                if(list == null || list.isEmpty()){
                    sendFail(goName, failCb, "no_result");
                    return;
                }
                Address a = list.get(0);
                JSONObject obj = new JSONObject();
                obj.put("country", safe(a.getCountryName()));
                obj.put("isoCountryCode", safe(a.getCountryCode()));
                obj.put("admin", safe(a.getAdminArea()));
                obj.put("locality", safe(a.getLocality()));
                obj.put("subLocality", safe(a.getSubLocality()));
                obj.put("thoroughfare", safe(a.getThoroughfare()));
                obj.put("subThoroughfare", safe(a.getSubThoroughfare()));
                obj.put("feature", safe(a.getFeatureName()));
                obj.put("postalCode", safe(a.getPostalCode()));
                String fullLine = safe(a.getAddressLine(0));
                if(fullLine.isEmpty()){
                    fullLine = buildFullLine(a);
                }
                obj.put("fullLine", fullLine);
                obj.put("formatted", buildFormatted(a));
                sendSuccess(goName, successCb, obj.toString());
            } catch (Exception e){
                sendFail(goName, failCb, "exception:" + e.getMessage());
            }
        }).start();
    }

    private static String buildFullLine(Address a){
        String[] parts = new String[]{safe(a.getFeatureName()), safe(a.getSubThoroughfare()), safe(a.getThoroughfare()), safe(a.getSubLocality()), safe(a.getLocality()), safe(a.getAdminArea()), safe(a.getCountryName())};
        StringBuilder sb = new StringBuilder();
        for(String p: parts){ if(p.length()>0){ if(sb.length()>0) sb.append(", "); sb.append(p);} }
        return sb.toString();
    }

    private static String buildFormatted(Address a){
        String[] parts = new String[]{safe(a.getCountryName()), safe(a.getAdminArea()), safe(a.getLocality()), safe(a.getSubLocality()), safe(a.getThoroughfare()), safe(a.getSubThoroughfare()), safe(a.getFeatureName())};
        StringBuilder sb = new StringBuilder();
        for(String p: parts){ if(p.length()>0){ if(sb.length()>0) sb.append(' '); sb.append(p);} }
        return sb.toString();
    }

    private static String safe(String s){ return s == null ? "" : s; }

    private static void sendSuccess(final String go, final String cb, final String msg){
        mainHandler.post(() -> UnityPlayer.UnitySendMessage(go, cb, msg));
    }

    private static void sendFail(final String go, final String cb, final String msg){
        mainHandler.post(() -> UnityPlayer.UnitySendMessage(go, cb, msg));
    }
}
