using System;
using System.Threading;
using UnityEngine;

public abstract class ManagerBase : MonoBehaviour
{
    public EManagerAsyncState State { get; private set; }
    
    
    private bool m_InitAsyncComplete = false;
    
    public async Awaitable InitAsync(CancellationToken token = default)
    {
        if (State != EManagerAsyncState.None || m_InitAsyncComplete)
        {
            HLog.Common.Warning("Manager already initialized");
            return;
        }

        try
        {
            State = EManagerAsyncState.InitAsync;
            await InitInternalAsync(token);

            State = EManagerAsyncState.InitComplete;
            m_InitAsyncComplete = true;
        }
        catch
        {
            State = EManagerAsyncState.Error;
            m_InitAsyncComplete = false;
            HLog.Common.Error($"Init Error! {GetType().Name}");
            throw;
        }

    }

    // 매니저가 따로 Init 정의가 가능하게 가상화
    public virtual bool IsInit()
    {
        return m_InitAsyncComplete && State == EManagerAsyncState.InitComplete;
    }
    
    
    public void Release()
    {
        if (State == EManagerAsyncState.None)
        {
            HLog.Common.Warning("Manager initialize error");
            return;
        }
        
        ReleaseInternal();
        State = EManagerAsyncState.None;
        m_InitAsyncComplete = false;
    }

    protected virtual void ReleaseInternal()
    {
        
    }
    
    protected abstract Awaitable InitInternalAsync(CancellationToken token = default);
}
