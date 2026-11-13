using System;
using System.Threading;
using UnityEngine;

public class BootPoint : MonoBehaviour
{
    private async void Awake()
    {
        try
        {
            HLog.Common.Log("Init Game!");
            await Managers.Init(Application.exitCancellationToken);
            HLog.Common.Log("Init Complete!!!");
        }
        catch (OperationCanceledException)
        {
            QuitWithBoot();
        }
        catch (Exception e)
        {
            // todo 안내후 게임 종료
            HLog.Common.Error($"Error!! - {e}");
            
            QuitWithBoot();
        }
    }

    private void QuitWithBoot()
    {
        if (Managers.Instance)
        {
            Managers.Instance.ReleaseAll();
        }

        // 임시
        Application.Quit();
    }
}