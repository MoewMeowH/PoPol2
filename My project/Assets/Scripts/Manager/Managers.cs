using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

public class Managers : MonoBehaviour
{
    private static Managers s_Instance;
    public static Managers Instance => s_Instance;
    
    private readonly Dictionary<Type, ManagerBase> m_Managers = new Dictionary<Type, ManagerBase>();
    private readonly List<Type> m_ManagerOrder = new List<Type>();
    
    public static Awaitable Init(CancellationToken token = default)
    {
        if (s_Instance)
        {
            HLog.Common.Error("Managers already initialized");
            return Helpers.CompletedAwaitable();
        }

        // Manager 오브젝트 생성
        var go = new GameObject("Managers", typeof(Managers));
        var cached = go.GetComponent<Managers>();
        s_Instance = cached;
        DontDestroyOnLoad(go);

        return cached.RegisterManager(token);
    }

    public static bool TryGet<T>(out T res) where T : ManagerBase
    {
        res = null;
        if (!s_Instance)
            return false;

        if (s_Instance.m_Managers.TryGetValue(typeof(T), out var @base))
            res = @base as T;

        if (res && !res.IsInit())
        {
            HLog.Common.Error($"{typeof(T).Name} Manager not init");
            res = null;
            return false;
        }
        return res;
    }
    
    private async Awaitable RegisterManager(CancellationToken token = default)
    {
        await Register<ResourceMgr>(token);
    }

    private async Awaitable Register<T>(CancellationToken token = default) where T : ManagerBase
    {
        var type = typeof(T);
        if (m_Managers.ContainsKey(type))
        {
            HLog.Common.Error($"Manager already exists {type}");
            return;
        }
     
        // Manager 자식 객체 생성
        var go = new GameObject($"{type.Name}", type);
        go.transform.SetParent(transform);
        var comp = go.GetComponent<T>();
        
        m_Managers.Add(type, comp);
        m_ManagerOrder.Add(type);
        await comp.InitAsync(token);
    }

    public void ReleaseAll()
    {
        for (var i = m_ManagerOrder.Count - 1; i >= 0; i--)
        {
            var type = m_ManagerOrder[i];
            if (m_Managers.TryGetValue(type, out var @base))
                @base.Release();
        }

        m_ManagerOrder.Clear();
        m_Managers.Clear();
    }

    private void OnDestroy()
    {
        ReleaseAll();
    }

    #region EditorOnly

#if UNITY_EDITOR

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ClearStatics()
    {
        s_Instance = null;
    }
    
#endif

    #endregion
}