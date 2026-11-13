using System;
using System.Threading;
using UnityEngine;

// todo -> 추후 설정창 등에서 모델 변경 감지 후 없으면 다운로드 안내
public class ResourceMgr : ManagerBase
{
    protected override Awaitable InitInternalAsync(CancellationToken token = default)
    {
        // todo - contents directory
        return Helpers.CompletedAwaitable();
    }
}