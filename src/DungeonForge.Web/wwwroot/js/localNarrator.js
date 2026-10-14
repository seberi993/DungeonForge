// In-browser narrator: runs a small instruct model with WebLLM (WebGPU). Weights are fetched once from the model CDN
// by WebLLM and cached by the browser; nothing here talks to the DungeonForge server. The prompts come from the server
// (see NarrationBrief) and the text goes back through the C# client, so game rules never depend on this file.

// Pinned build. Bump deliberately and re-check MODEL_CANDIDATES against that release's prebuilt model list.
const WEBLLM_URL = "https://cdn.jsdelivr.net/npm/@mlc-ai/web-llm@0.2.79/+esm";

// First one present in the pinned release's prebuilt list wins (smallest download that still follows instructions).
const MODEL_CANDIDATES = [
    "Qwen2.5-1.5B-Instruct-q4f16_1-MLC",
    "Llama-3.2-1B-Instruct-q4f16_1-MLC",
    "Qwen2.5-0.5B-Instruct-q4f16_1-MLC",
];

const OPT_IN_KEY = "dungeonforge.localNarrator";

let webllm = null;
let engine = null;
let loading = null;
let busy = Promise.resolve();

export async function isSupported() {
    try {
        if (!("gpu" in navigator)) return false;
        return (await navigator.gpu.requestAdapter()) !== null;
    } catch {
        return false;
    }
}

export function getOptIn() {
    try { return localStorage.getItem(OPT_IN_KEY) === "1"; } catch { return false; }
}

export function setOptIn(value) {
    try { localStorage.setItem(OPT_IN_KEY, value ? "1" : "0"); } catch { /* private mode: just not remembered */ }
}

// dotnet: DotNetObjectReference exposing OnProgress(int percent, string text).
export async function load(dotnet) {
    if (engine) return;
    loading ??= (async () => {
        webllm ??= await import(WEBLLM_URL);
        const available = new Set(webllm.prebuiltAppConfig.model_list.map(m => m.model_id));
        const modelId = MODEL_CANDIDATES.find(id => available.has(id));
        if (!modelId) throw new Error("No supported small model in this WebLLM release.");
        engine = await webllm.CreateMLCEngine(modelId, {
            initProgressCallback: report =>
                dotnet.invokeMethodAsync("OnProgress", Math.round((report.progress ?? 0) * 100), report.text ?? ""),
        });
    })();
    try {
        await loading;
    } finally {
        loading = null;
    }
}

// One generation at a time: the GPU is shared and a second call would only slow both down.
export function generate(system, user, jsonSchema, maxTokens) {
    const run = async () => {
        if (!engine) throw new Error("The local model is not loaded.");
        await engine.resetChat();
        const request = {
            messages: [
                { role: "system", content: system },
                { role: "user", content: user },
            ],
            temperature: 0.8,
            max_tokens: maxTokens || 600,
        };
        if (jsonSchema) request.response_format = { type: "json_object", schema: jsonSchema };
        const reply = await engine.chat.completions.create(request);
        return reply.choices?.[0]?.message?.content ?? "";
    };
    const result = busy.then(run, run);
    busy = result.catch(() => { });
    return result;
}
