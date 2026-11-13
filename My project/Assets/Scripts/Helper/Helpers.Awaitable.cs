using System;
using System.Threading;
using UnityEngine;

public partial class Helpers
{
    private static readonly AwaitableCompletionSource s_CompletedAwaitable = new AwaitableCompletionSource();

    public static async Awaitable AwaitableUntil(Func<bool> condition, CancellationToken cancellationToken)
    {
        while(!condition()){
            cancellationToken.ThrowIfCancellationRequested();
            await Awaitable.NextFrameAsync(cancellationToken);
        }
    }
    
    public static Awaitable CompletedAwaitable()
    {
        s_CompletedAwaitable.Reset();
        s_CompletedAwaitable.SetResult();
        return s_CompletedAwaitable.Awaitable;
    }
}