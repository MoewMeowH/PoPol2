using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LLMUnity;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// LLMUnity 기술 검증용 실험 (게임 코드 아님).
/// Play 한 번에 모델 하나를 올려 속도 · 응답 원문 · grammar · 스트리밍 콜백을 재고 결과를 파일로 남긴다.
/// 프롬프트의 배경 · 능력치 이름은 시험용 임시 값이다.
/// </summary>
public class LlmUnityExperiment : MonoBehaviour
{
    [Header("실행 설정")]
    public string label = "gemma3-12b_gpu";
    public string modelPath = @"F:\AI\Models\gemma-3-12b-it-Q4_K_M.gguf";
    [Tooltip("GPU에 올릴 층 수 (0 = CPU만)")]
    public int numGPULayers = 99;
    public int contextSize = 8192;
    [Tooltip("켜면 프롬프트 1종을 1회만 돈다 (CPU 속도 비교용)")]
    public bool speedOnly = false;
    [Tooltip("켜면 여러 턴 흐름(요약본 방식 · 대화 기록 방식 · 같은 프롬프트 다시 시도)만 잰다")]
    public bool multiTurn = false;
    [Tooltip("끄면 요청마다 프롬프트 전체를 다시 처리한다 (여러 턴 시험의 A · B에만 적용)")]
    public bool cachePrompt = true;
    [Tooltip("여러 턴 시험 A(요약본 방식)의 턴 수. 이벤트 6개를 돌려 쓴다")]
    public int summaryTurns = 6;
    public int repeats = 3;
    public int grammarRuns = 10;
    public int numPredict = 400;
    public float temperature = 0.7f;
    [Tooltip("llama.cpp 상세 로그를 남긴다 (장치 · GPU에 올린 층 수 확인용)")]
    public bool verboseLog = false;
    public bool exitPlayModeWhenDone = true;

    const string SystemPrompt =
        "너는 텍스트 어드벤처 게임의 게임 마스터다. 주어진 상황을 한국어로 묘사한다. " +
        "주인공을 '당신'이라 부르고 현재형으로 쓴다. 지시가 없으면 3~5문장으로 쓴다. " +
        "주어지지 않은 선택지나 판정 결과를 지어내지 않는다.";

    const string PromptScene =
        "[장면] 비 오는 밤, 정전된 지하상가 입구. 주인공은 다친 다리를 끌고 들어온다. " +
        "안쪽에서 발전기 소리와 낯선 사람들의 목소리가 들린다.\n" +
        "[지시] 이 장면을 묘사하라.";

    const string PromptCheck =
        "[상황] 주인공이 잠긴 약국 셔터를 쇠지레로 뜯으려 했다.\n" +
        "[판정] 능력치 '힘' d8 = 3, 난이도 d6 = 5 → 실패.\n" +
        "[대가] 체력 -2.\n" +
        "[지시] 실패한 결과를 묘사하라. 수치는 말하지 않는다.";

    const string PromptChoices =
        "[상황] 발전기 옆에서 상인이 주인공을 경계하며 바라본다.\n" +
        "[행동 목록]\n1. 거래를 제안한다 (돈 필요)\n2. 몰래 지나간다 (판정: 민첩)\n3. 말을 걸어 정보를 묻는다\n" +
        "[지시] 각 행동을 주인공 시점의 선택지 문구로 한 줄씩 다시 써라. 번호는 유지하고 다른 말은 붙이지 않는다.";

    const string PromptJson =
        "[장면] 비 오는 밤, 정전된 지하상가 입구. 주인공은 다친 다리를 끌고 들어온다. " +
        "안쪽에서 발전기 소리와 낯선 사람들의 목소리가 들린다.\n" +
        "[행동 목록]\n1. 소리가 나는 쪽으로 간다\n2. 입구 근처에 숨어 지켜본다\n3. 다리를 먼저 살핀다\n" +
        "[지시] JSON 하나로만 답하라. narration = 장면 묘사 3~5문장, " +
        "choices = 행동 목록을 선택지 문구로 바꾼 3개, summary = 이번 턴을 한 문장으로 줄인 것.";

    const string JsonSchema =
        "{\"type\":\"object\",\"properties\":{" +
        "\"narration\":{\"type\":\"string\"}," +
        "\"choices\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"minItems\":3,\"maxItems\":3}," +
        "\"summary\":{\"type\":\"string\"}}," +
        "\"required\":[\"narration\",\"choices\",\"summary\"],\"additionalProperties\":false}";

    const string JsonInstruction =
        "[지시] JSON 하나로만 답하라. narration = 장면 묘사 3~5문장, " +
        "choices = 행동 목록을 선택지 문구로 바꾼 3개, summary = 이번 턴을 한 문장으로 줄인 것.";

    // 여러 턴 시험용 이벤트 (장면 + 행동 목록). 직전 턴의 선택 결과는 흉내 내지 않는다.
    static readonly string[] TurnEvents =
    {
        "[장면] 비 오는 밤, 정전된 지하상가 입구. 주인공은 다친 다리를 끌고 들어온다. 안쪽에서 발전기 소리와 낯선 사람들의 목소리가 들린다.\n" +
        "[행동 목록]\n1. 소리가 나는 쪽으로 간다\n2. 입구 근처에 숨어 지켜본다\n3. 다리를 먼저 살핀다",
        "[장면] 발전기 옆, 상인이 좌판을 펴고 주인공을 경계하며 바라본다. 좌판에는 통조림과 붕대가 놓여 있다.\n" +
        "[행동 목록]\n1. 거래를 제안한다\n2. 몰래 지나간다\n3. 말을 걸어 정보를 묻는다",
        "[장면] 상가 안쪽, 셔터가 반쯤 내려간 약국. 안에서 희미한 손전등 불빛이 움직인다.\n" +
        "[행동 목록]\n1. 셔터 밑으로 기어 들어간다\n2. 밖에서 안을 부른다\n3. 다른 가게를 찾아본다",
        "[장면] 약국 안, 선반 뒤에서 누군가 숨을 죽이고 있다. 바닥에는 쏟아진 약병이 굴러다닌다.\n" +
        "[행동 목록]\n1. 손을 들어 보이며 다가간다\n2. 약병부터 챙긴다\n3. 조용히 물러난다",
        "[장면] 갑자기 전기가 돌아오며 상가 전체에 경보음이 울린다. 사람들이 출구 쪽으로 뛰기 시작한다.\n" +
        "[행동 목록]\n1. 사람들을 따라 뛴다\n2. 경보가 울리는 곳을 확인한다\n3. 약국 안에 숨는다",
        "[장면] 비상계단 문 앞. 문은 사슬로 감겨 있고, 뒤에서는 발소리가 가까워진다.\n" +
        "[행동 목록]\n1. 사슬을 끊으려 한다\n2. 돌아서서 맞선다\n3. 환기구를 살핀다",
    };

    [Serializable]
    class TurnJson
    {
        public string narration;
        public string[] choices;
        public string summary;
    }

    class RunStat
    {
        public double firstMs, totalMs;
        public int calls, offMainThread, notCumulative, tokens;
        public string text = "";
        public List<string> samples = new List<string>();
        public double GenTokPerSec => totalMs > firstMs ? tokens / ((totalMs - firstMs) / 1000.0) : 0;
        public double AllTokPerSec => totalMs > 0 ? tokens / (totalMs / 1000.0) : 0;
    }

    LLM llm;
    LLMAgent agent;
    string resultPath;
    StreamWriter logWriter;
    readonly object logLock = new object();
    string status = "준비 중";
    string screenText = "";
    GUIStyle style;

    async void Start()
    {
        try
        {
            await Run();
        }
        catch (Exception e)
        {
            Write($"\n**예외**: `{e}`\n");
            Debug.LogException(e);
        }
        finally
        {
            Write("\n(끝)\n");
            status = "끝";
            Application.logMessageReceivedThreaded -= OnLog;
            lock (logLock)
            {
                logWriter?.Dispose();
                logWriter = null;
            }
#if UNITY_EDITOR
            if (exitPlayModeWhenDone) UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
    }

    async Task Run()
    {
        string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../docs/worklog/llmunity_raw"));
        Directory.CreateDirectory(outDir);
        resultPath = Path.Combine(outDir, label + ".md");
        logWriter = new StreamWriter(Path.Combine(outDir, label + "_unity.log"), false, new UTF8Encoding(false));
        Application.logMessageReceivedThreaded += OnLog;

        File.WriteAllText(resultPath,
            $"# {label}\n\n" +
            $"- 시각: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
            $"- Unity {Application.unityVersion} · {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}, VRAM {SystemInfo.graphicsMemorySize}MB)\n" +
            $"- 모델: `{modelPath}` ({new FileInfo(modelPath).Length / 1073741824.0:F2}GB)\n" +
            $"- numGPULayers {numGPULayers} · contextSize {contextSize} · numPredict {numPredict} · temperature {temperature} · verboseLog {verboseLog}\n");

        // 정적 필드만 바꾼다 (PlayerPrefs에 저장하는 SetDebugMode는 쓰지 않는다)
        LLMUnitySetup.DebugMode = verboseLog ? LLMUnitySetup.DebugModeType.Debug : LLMUnitySetup.DebugModeType.All;

        var go = new GameObject("LLM");
        go.SetActive(false);
        llm = go.AddComponent<LLM>();
        llm.dontDestroyOnLoad = false;
        llm.numGPULayers = numGPULayers;
        llm.model = modelPath;
        llm.contextSize = contextSize;
        agent = go.AddComponent<LLMAgent>();
        agent.llm = llm;
        agent.systemPrompt = SystemPrompt;
        agent.numPredict = numPredict;
        agent.temperature = temperature;

        status = "모델 올리는 중";
        var loadWatch = Stopwatch.StartNew();
        go.SetActive(true);
        await llm.WaitUntilReady();
        Write($"- 올리기: {loadWatch.Elapsed.TotalSeconds:F1}초 · started {llm.started} · failed {llm.failed} · architecture `{llm.architecture}`\n");
        if (!llm.started) return;

        // 첫 요청은 시스템 프롬프트 처리 · 초기화가 섞이므로 따로 잰다
        await Section("워밍업 (첫 요청)", PromptScene, 1, false);

        if (speedOnly)
        {
            await Section("장면 묘사", PromptScene, 1, false);
            return;
        }

        if (multiTurn)
        {
            await MultiTurn();
            return;
        }

        await Section("장면 묘사", PromptScene, repeats, false);
        await Section("판정 결과 묘사", PromptCheck, repeats, false);
        await Section("선택지 문구", PromptChoices, repeats, false);
        await Section("JSON 지시만 (grammar 없음)", PromptJson, repeats, true);

        agent.grammar = JsonSchema;
        await Section("JSON schema grammar", PromptJson, grammarRuns, true);
        agent.grammar = "";
    }
    
    async Awaitable Section(string title, string query, int count, bool parseJson)
    {
        Write($"\n## {title}\n\n```\n{query}\n```\n");
        int parsed = 0, escaped = 0;
        double sumGen = 0, sumFirst = 0;
        for (int i = 1; i <= count; i++)
        {
            status = $"{label} — {title} {i}/{count}";
            RunStat s = await OneChat(query);
            sumGen += s.GenTokPerSec;
            sumFirst += s.firstMs;

            var sb = new StringBuilder();
            sb.Append($"\n### {title} — {i}회\n\n");
            sb.Append($"- 첫 콜백 {s.firstMs:F0}ms · 전체 {s.totalMs:F0}ms · 토큰 {s.tokens} · 생성 {s.GenTokPerSec:F1} tok/s (전체 기준 {s.AllTokPerSec:F1})\n");
            sb.Append($"- 콜백 {s.calls}회 · 메인 스레드 밖 {s.offMainThread} · 누적 아님 {s.notCumulative}\n");
            if (parseJson)
            {
                bool ok = TryParse(s.text, out string why);
                if (ok) parsed++;
                bool hasEscape = s.text.Contains("\\u");
                if (hasEscape) escaped++;
                sb.Append($"- JSON 파싱: {(ok ? "성공" : "실패 — " + why)} · \\u 이스케이프: {(hasEscape ? "있음" : "없음")}\n");
            }
            if (i == 1)
            {
                sb.Append("- 콜백이 받은 글 (1 · 2 · 3 · 10번째):\n");
                foreach (string sample in s.samples) sb.Append($"  - `{sample.Replace("\n", "\\n")}`\n");
            }
            sb.Append($"\n```\n{s.text}\n```\n");
            Write(sb.ToString());
        }
        string summary = $"\n**{title} 평균**: 첫 콜백 {sumFirst / count:F0}ms · 생성 {sumGen / count:F1} tok/s";
        if (parseJson) summary += $" · 파싱 성공 {parsed}/{count} · \\u 이스케이프 {escaped}/{count}";
        Write(summary + "\n");
    }

    // 턴마다 프롬프트가 달라지는 흐름에서 첫 콜백 지연이 나오는지 잰다.
    async Task MultiTurn()
    {
        agent.grammar = JsonSchema;
        agent.cachePrompt = cachePrompt;
        Write($"\n- cachePrompt {cachePrompt} (A · B)\n");

        // A. 요약본 방식: 기록을 쌓지 않고, 지금까지의 요약 + 이번 이벤트를 매 턴 새로 보낸다
        Write("\n## A. 요약본 방식 (턴마다 요약 + 새 이벤트, 대화 기록 없음)\n");
        var summaries = new List<string>();
        for (int i = 0; i < summaryTurns; i++)
        {
            string summaryBlock = summaries.Count == 0 ? "(없음)" : "- " + string.Join("\n- ", summaries);
            string query = $"[지금까지 요약]\n{summaryBlock}\n{TurnEvents[i % TurnEvents.Length]}\n{JsonInstruction}";
            RunStat s = await Turn($"A-{i + 1}", query, false);
            try
            {
                TurnJson t = JsonUtility.FromJson<TurnJson>(s.text.Trim());
                if (t != null && !string.IsNullOrWhiteSpace(t.summary)) summaries.Add(t.summary.Trim());
            }
            catch (ArgumentException)
            {
                // 파싱 실패는 Turn이 이미 적었다. 요약 없이 다음 턴으로 간다
            }
        }

        // B. 대화 기록 방식: 기록을 지우지 않고 이벤트만 이어 보낸다 (앞부분이 같고 뒤가 늘어난다)
        Write("\n## B. 대화 기록 방식 (기록 유지, 이벤트만 보냄)\n");
        await agent.ClearHistory();
        for (int i = 0; i < TurnEvents.Length; i++)
            await Turn($"B-{i + 1}", $"{TurnEvents[i]}\n{JsonInstruction}", true);

        // C. 실패 뒤 같은 프롬프트로 다시 시도하는 경우. cachePrompt를 끄면 달라지는지 본다
        Write("\n## C. 같은 프롬프트 다시 시도\n");
        string retryA = $"[지금까지 요약]\n(없음)\n{TurnEvents[2]}\n{JsonInstruction}";
        string retryB = $"[지금까지 요약]\n(없음)\n{TurnEvents[4]}\n{JsonInstruction}";
        agent.cachePrompt = true;
        await Turn("C-1 처음 (cachePrompt 켬)", retryA, false);
        await Turn("C-2 같은 것 다시 (cachePrompt 켬)", retryA, false);
        agent.cachePrompt = false;
        await Turn("C-3 처음 (cachePrompt 끔)", retryB, false);
        await Turn("C-4 같은 것 다시 (cachePrompt 끔)", retryB, false);
        agent.cachePrompt = true;

        agent.grammar = "";
    }

    async Task<RunStat> Turn(string title, string query, bool keepHistory)
    {
        status = $"{label} — {title}";
        int promptTokens = (await agent.Tokenize(query)).Count;
        RunStat s = await OneChat(query, keepHistory);
        bool ok = TryParse(s.text, out string why);
        Write($"\n### {title}\n\n" +
              $"- 첫 콜백 {s.firstMs:F0}ms · 전체 {s.totalMs:F0}ms · 질의 토큰 {promptTokens} · 응답 토큰 {s.tokens} · 생성 {s.GenTokPerSec:F1} tok/s\n" +
              $"- JSON 파싱: {(ok ? "성공" : "실패 — " + why)}\n" +
              $"\n```\n{query}\n```\n\n```\n{s.text}\n```\n");
        return s;
    }

    async Task<RunStat> OneChat(string query, bool keepHistory = false)
    {
        if (!keepHistory) await agent.ClearHistory();
        // seed 0은 고정 seed로 동작해 같은 프롬프트에 같은 답이 나온다 → 요청마다 바꾼다
        agent.seed = UnityEngine.Random.Range(1, int.MaxValue);
        var s = new RunStat();
        string prev = "";
        int mainThread = Thread.CurrentThread.ManagedThreadId;
        screenText = "";
        var watch = Stopwatch.StartNew();
        string result = await agent.Chat(query, text =>
        {
            if (s.calls == 0) s.firstMs = watch.Elapsed.TotalMilliseconds;
            s.calls++;
            if (Thread.CurrentThread.ManagedThreadId != mainThread) s.offMainThread++;
            if (!text.StartsWith(prev, StringComparison.Ordinal)) s.notCumulative++;
            if (s.calls <= 3 || s.calls == 10) s.samples.Add(text);
            prev = text;
            screenText = ExtractNarration(text);
        }, null, keepHistory);
        s.totalMs = watch.Elapsed.TotalMilliseconds;
        s.text = result ?? "";
        s.tokens = (await agent.Tokenize(s.text)).Count;
        return s;
    }

    static bool TryParse(string json, out string why)
    {
        try
        {
            TurnJson t = JsonUtility.FromJson<TurnJson>(json.Trim());
            if (t == null) why = "null";
            else if (string.IsNullOrWhiteSpace(t.narration)) why = "narration 없음";
            else if (t.choices == null || t.choices.Length != 3) why = $"choices {(t.choices == null ? 0 : t.choices.Length)}개";
            else if (string.IsNullOrWhiteSpace(t.summary)) why = "summary 없음";
            else why = "";
        }
        catch (Exception e)
        {
            why = e.GetType().Name;
        }
        return why == "";
    }

    // 쓰는 중인 JSON에서 narration 값만 꺼낸다 (화면에 흘려 보이기용). JSON이 아니면 그대로.
    static string ExtractNarration(string text)
    {
        const string key = "\"narration\"";
        int k = text.IndexOf(key, StringComparison.Ordinal);
        if (k < 0) return text;
        int open = text.IndexOf('"', k + key.Length);
        if (open < 0) return "";
        var sb = new StringBuilder();
        for (int i = open + 1; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"') break;
            if (c == '\\' && i + 1 < text.Length)
            {
                char n = text[++i];
                sb.Append(n == 'n' ? '\n' : n);
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    void Write(string text)
    {
        if (resultPath != null) File.AppendAllText(resultPath, text);
    }

    void OnLog(string message, string stackTrace, LogType type)
    {
        lock (logLock)
        {
            if (logWriter == null) return;
            logWriter.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [{type}] {message}");
            logWriter.Flush();
        }
    }

    void OnGUI()
    {
        if (style == null) style = new GUIStyle(GUI.skin.label) { fontSize = 20, wordWrap = true };
        GUI.Label(new Rect(20, 20, Screen.width - 40, 40), status, style);
        GUI.Label(new Rect(20, 70, Screen.width - 40, Screen.height - 90), screenText, style);
    }
}
