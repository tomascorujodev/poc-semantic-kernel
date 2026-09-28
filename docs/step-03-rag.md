# Step 3 of poc-semantic-kernel: RAG over Documents

> **Goal of this step:** let the assistant answer questions about the store's policies (returns, warranty, shipping) using **our own documents**, and cite the source file.
>
> **Result:** ✅ Working. `dotnet run -- 3` ingests 13 chunks, and the model answers "Can I return an opened laptop?" with the 15% restocking fee from `returns-policy.md`.

---

## Contents
1. [What is RAG?](#1-what-is-rag)
2. [Key concepts](#2-key-concepts)
3. [Architecture of this step](#3-architecture-of-this-step)
4. [Prerequisites](#4-prerequisites)
5. [Packages](#5-packages)
6. [The code explained](#6-the-code-explained)
7. [Running the app](#7-running-the-app)
8. [Troubleshooting: problems we hit and how we fixed them](#8-troubleshooting-problems-we-hit-and-how-we-fixed-them)
9. [Lessons learned](#9-lessons-learned)
10. [Glossary](#10-glossary)
11. [Next steps](#11-next-steps)

---

## 1. What is RAG?

**RAG (Retrieval-Augmented Generation)** means: **retrieve** relevant text from your own data, then let the model **generate** an answer from it.

An LLM only knows what was in its training data. It does not know *your* return policy. Without RAG, it either says "I don't know" or, worse, **invents** a plausible policy (a *hallucination*).

With RAG, we search our documents first and put the best matches in front of the model, with a rule: *answer only from these*.

```
Without RAG:  question ──► model ──► answer from training data (may be invented)
With RAG:     question ──► search your docs ──► model + relevant text ──► grounded answer + source
```

---

## 2. Key concepts

### General AI concepts

| Concept | Meaning |
|---|---|
| **Embedding** | A list of numbers (a *vector*) that represents the meaning of a text. `text-embedding-3-small` produces 1536 numbers. |
| **Semantic search** | Search by meaning instead of by keywords: texts with similar meanings have vectors that are close together. "opened laptop" finds "Condition of returned items" even with different words. |
| **Vector store** | A database that stores vectors and finds the closest ones to a query vector. |
| **Chunk** | A small piece of a document. We store and search chunks, not whole files. |
| **Chunking** | Splitting documents into chunks. Small, focused chunks match better and cost fewer tokens. |
| **Grounding** | Tying the model's answer to real sources, to reduce hallucinations. |
| **Citation** | Showing which source a fact came from, e.g. `[returns-policy.md]`. |

### Semantic Kernel / .NET concepts used in this step

| Concept | What it is | Where it is in the code |
|---|---|---|
| **Embedding generator** | The AI service that turns text into vectors (`IEmbeddingGenerator<string, Embedding<float>>`) | `KernelFactory.cs` → `AddAzureOpenAIEmbeddingGenerator(...)` |
| **Record model** | A C# class that describes what is stored, using attributes | `Rag/DocumentChunk.cs` |
| **`[VectorStoreKey]` / `[VectorStoreData]` / `[VectorStoreVector]`** | Mark the key, the normal data fields and the vector field | `Rag/DocumentChunk.cs` |
| **Collection** | A "table" of records in the vector store | `InMemoryCollection<string, DocumentChunk>` in `Rag/PolicyStore.cs` |
| **`UpsertAsync`** | Insert or update records (embeds them automatically) | `Rag/DocumentIngestor.cs` |
| **`SearchAsync`** | Embed the query and return the closest records with a score | `Plugins/DocsPlugin.cs` |
| **Retrieval as a plugin** | Search is exposed as a normal `[KernelFunction]`, so the model decides when to search | `Plugins/DocsPlugin.cs` |

> ℹ️ The vector store abstractions live in **`Microsoft.Extensions.VectorData`**, a .NET library shared by Semantic Kernel, Microsoft Agent Framework and other tools. The same code works with other stores (Azure AI Search, Qdrant, Postgres…) by changing only the collection class.

---

## 3. Architecture of this step

```
 INGESTION (at startup)                              QUERY (every question)
 ┌──────────────────┐  split by "## "  ┌──────────┐  embed  ┌──────────────────────────┐
 │ Data/Docs/*.md   │─────────────────►│ 13 chunks│────────►│ InMemoryCollection        │
 │ returns-policy   │                  └──────────┘         │ "store-policies"          │
 │ warranty         │                                       │ (text + 1536-number vector)│
 │ shipping         │                                       └────────────▲─────────────┘
 └──────────────────┘                                                    │ SearchAsync(top: 3)
                                                                         │
 User: "Can I return an opened laptop?"                                  │
   │                                                                     │
   ▼                                                                     │
 Chat model (gpt-4.1-mini) ──tool call──► Docs.SearchPolicies("return opened laptop")
   ▲                                             │
   └──────── top 3 chunks + source file ─────────┘
   │
   ▼
 "Yes, with a 15% restocking fee [returns-policy.md]"
```

Both the **ingestion** and the **query** call the embedding model (`text-embedding-3-small`). The **answer** comes from the chat model (`gpt-4.1-mini`). The Kernel now holds **two AI services**.

---

## 4. Prerequisites

Everything from Step 1 and Step 2, plus an **embedding model deployment**:

1. In the Foundry portal (https://ai.azure.com): open the project, go to **Models + endpoints → + Deploy model → Deploy base model**.
2. Choose `text-embedding-3-small`, deployment type **Global Standard**. Deployment name: `text-embedding-3-small`.
3. Save the deployment name:
   ```powershell
   cd src/PocSemanticKernel
   dotnet user-secrets set "AzureOpenAI:EmbeddingDeployment" "text-embedding-3-small"
   ```

> ⚠️ Saving the secret does **not** deploy the model. Both are needed. The endpoint and key are the same as for the chat model (same resource).

### Settings (full list after Step 3)

| Key | Example value |
|---|---|
| `AzureOpenAI:Endpoint` | `https://<resource-name>.openai.azure.com/` |
| `AzureOpenAI:ApiKey` | `********` |
| `AzureOpenAI:ChatDeployment` | `gpt-4.1-mini` |
| `AzureOpenAI:EmbeddingDeployment` | `text-embedding-3-small` |

---

## 5. Packages

| Package | Version | Why |
|---|---|---|
| `CommunityToolkit.VectorData.InMemory` | 1.0.1 | An in-memory vector store. No Azure resource needed. Brings `Microsoft.Extensions.VectorData.Abstractions` 10.8.2. |

```powershell
dotnet add package CommunityToolkit.VectorData.InMemory
```

> ⚠️ **Don't use `Microsoft.SemanticKernel.Connectors.InMemory`.** Its last version (1.74.0-preview) is not compatible with Semantic Kernel 1.80. See [Troubleshooting #1](#8-troubleshooting-problems-we-hit-and-how-we-fixed-them).

The documents are copied next to the `.exe` by this entry in `PocSemanticKernel.csproj`:
```xml
<None Include="Data\Docs\*.md" CopyToOutputDirectory="PreserveNewest" />
```

### Project structure after Step 3

```
poc-semantic-kernel/
├─ docs/
│  ├─ step-01-setup.md
│  └─ step-03-rag.md               ← this document
└─ src/PocSemanticKernel/
   ├─ Data/Docs/                   ← the store's documents (source of truth for RAG)
   │  ├─ returns-policy.md
   │  ├─ warranty.md
   │  └─ shipping.md
   ├─ Demos/
   │  ├─ ChatLoop.cs               ← shared chat loop (Step 2 and Step 3)
   │  ├─ ChatWithPluginsDemo.cs    ← Step 2
   │  ├─ HelloPromptDemo.cs        ← Step 1
   │  └─ RagDemo.cs                ← Step 3
   ├─ Filters/LoggingFilter.cs
   ├─ Plugins/
   │  ├─ DocsPlugin.cs             ← Step 3: search as a tool
   │  ├─ OrdersPlugin.cs
   │  └─ TimePlugin.cs
   ├─ Rag/
   │  ├─ DocumentChunk.cs          ← record model
   │  └─ DocumentIngestor.cs       ← chunking + upsert
   ├─ KernelFactory.cs             ← + embedding service
   └─ Program.cs                   ← + menu option 3
```

---

## 6. The code explained

### 6.1 The documents (`Data/Docs/*.md`)

Three short Markdown files for the fake store. They contain **specific, made-up facts** ("15% restocking fee", "3-year warranty for Pro laptops", "no shipping outside the EU"). The model cannot know them from training, so if the answer contains them, it came from our documents.

### 6.2 The embedding service (`KernelFactory.cs`)

```csharp
#pragma warning disable SKEXP0010
builder.AddAzureOpenAIEmbeddingGenerator(embeddingDeployment, endpoint, apiKey);
#pragma warning restore SKEXP0010
```
- Registers an `IEmbeddingGenerator<string, Embedding<float>>` in the Kernel, next to the chat service.
- `SKEXP0010` marks the API as **experimental**: it works, but may change in future versions. SK makes this a build error until you opt in with `#pragma warning disable`. We disable it only around that one line.

### 6.3 The record model (`Rag/DocumentChunk.cs`)

```csharp
public sealed class DocumentChunk
{
    [VectorStoreKey]  public required string Id { get; init; }       // "returns-policy.md#2"
    [VectorStoreData] public required string Source { get; init; }   // for citations
    [VectorStoreData] public required string Heading { get; init; }  // section title
    [VectorStoreData] public required string Text { get; init; }     // chunk content

    [VectorStoreVector(1536)]
    public string Embedding => $"{Heading}\n{Text}";
}
```
- The attributes tell the vector store what each property is.
- `1536` must match the embedding model's output size (`text-embedding-3-small` = 1536).
- **The vector property is a `string`, not numbers.** Because the collection has an `EmbeddingGenerator`, the store calls the embedding model for us, on `UpsertAsync` (for the chunks) and on `SearchAsync` (for the query). We never handle the numbers ourselves.
- We embed **heading + text**, so the section title also helps matching.

### 6.4 Chunking and ingestion (`Rag/DocumentIngestor.cs`)

```csharp
await collection.EnsureCollectionExistsAsync();
var chunks = Directory.GetFiles(folder, "*.md").SelectMany(ChunkFile).ToList();
await collection.UpsertAsync(chunks);   // one embedding call per chunk happens here
```
- `ChunkFile` splits each file at every `## ` heading, so **one section = one chunk**. The `# Title` part before the first `## ` is skipped.
- This simple strategy works because our documents are short and well structured. Real documents (PDFs, long pages) usually need **size-based chunking with overlap**, e.g. ~500 tokens per chunk with ~50 tokens repeated between chunks.
- **In-memory data is lost when the app stops**, so ingestion runs at every start. That's fine for 13 chunks; a persistent store comes in a later step.

### 6.5 The collection (`Rag/PolicyStore.cs`)

> ℹ️ In Step 4 this setup was moved from `Demos/RagDemo.cs` to `Rag/PolicyStore.cs`, so Steps 3 and 4 share it.

```csharp
var embeddings = kernel.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
var collection = new InMemoryCollection<string, DocumentChunk>(
    "store-policies", new() { EmbeddingGenerator = embeddings });
```
- `<string, DocumentChunk>` = key type + record type.
- The embedding generator is taken from the Kernel and given to the collection.

### 6.6 Retrieval as a plugin (`Plugins/DocsPlugin.cs`)

```csharp
[KernelFunction, Description("Searches the store's policy documents: returns, refunds, warranty, shipping and order statuses.")]
public async Task<IReadOnlyList<DocSearchResult>> SearchPolicies(
    [Description("What to look for, written as a short question or keywords, e.g. 'return opened laptop fee'")] string query)
{
    var results = new List<DocSearchResult>();
    await foreach (var r in collection.SearchAsync(query, top: 3))
        results.Add(new(r.Record.Source, r.Record.Heading, r.Record.Text, r.Score));
    return results;
}
```
- This is **the same idea as Step 2**: search is just another tool. The model decides *when* to search and *what* to search for. In our test it rewrote "Can I return an opened laptop?" as `return opened laptop`.
- `top: 3` returns the 3 closest chunks. `Score` is the similarity (higher = closer for cosine similarity).
- We return `Source` so the model can cite it.

> ℹ️ **Alternative:** SK has a built-in `VectorStoreTextSearch` that creates a search plugin for you. We wrote our own to see exactly what happens.

### 6.7 Grounding rules (system prompt in `Demos/RagDemo.cs`)

```
For any question about returns, refunds, warranty, shipping or order statuses,
ALWAYS call Docs-SearchPolicies first and answer ONLY from its results.
Cite the source file in brackets after each fact, e.g. [returns-policy.md].
If the documents do not contain the answer, say you don't know. Never invent policies.
```
- **"ALWAYS call … first"**: makes sure the model searches instead of answering from training data.
- **"ONLY from its results"** + **"say you don't know"**: grounding.
- **"Cite the source"**: citations.
- `Docs-SearchPolicies` is the name the model sees: *plugin name* + `-` + *function name*.

### 6.8 Shared chat loop (`Demos/ChatLoop.cs`)

Step 2 and Step 3 use the same loop (ChatHistory + automatic function calling + streaming), so it was moved from `ChatWithPluginsDemo.cs` into `ChatLoop.RunAsync(kernel, systemPrompt, intro)`. Each demo now only registers its plugins and passes its own system prompt.

---

## 7. Running the app

```powershell
cd C:\Proyectos-2\poc-semantic-kernel\src\PocSemanticKernel
dotnet run -- 3
```

**Actual output:**
```
Ingested 13 chunks from ...\bin\Debug\net10.0\Data\Docs

You > Can I return an opened laptop?
AI  >   🔧 Docs.SearchPolicies(query=return opened laptop)
  ↳ [{"Source":"returns-policy.md","Heading":"Condition of returned items","Text":"Unopened products get a full refund. Opened laptops, tablets and monitors have a 15% restocking fee. ...
You can return an opened laptop, but there will be a 15% restocking fee for opened laptops. Unopened products get
a full refund. Accessories like cables, docks, and keyboards can be returned without a fee even if opened.
Headphones and earbuds cannot be returned once opened for hygiene reasons [returns-policy.md].
```

**What this shows:**
- `13 chunks` = 5 sections (returns) + 4 (warranty) + 4 (shipping).
- The best chunk ("Condition of returned items") was found **by meaning**: the question does not contain the words "condition" or "restocking".
- "15%" can only come from our document → the answer is **grounded**, with a **citation**.
- The headphones sentence came from another of the top 3 chunks ("Non-returnable items"). It is correct, just more than asked.
- The citation appears once at the end instead of after each fact. Acceptable with a single source; a stricter prompt could enforce per-fact citations.

### More test questions

| Question | Expected behaviour | Status |
|---|---|---|
| "Can I return an opened laptop?" | `SearchPolicies` → 15% fee, `[returns-policy.md]` | ✅ Verified |
| "Is order 40 still under warranty?" | Calls **both** `Orders.GetOrder` and `Docs.SearchPolicies`; order 40 is a "Laptop Pro 14" → 3-year warranty `[warranty.md]` | ⏳ Not tested yet |
| "Do you sell cars?" | Says it doesn't know / can't help; invents nothing | ⏳ Not tested yet |

---

## 8. Troubleshooting: problems we hit and how we fixed them

| # | Error | Cause | Fix |
|---|---|---|---|
| 1 | `System.TypeLoadException: Could not load type 'Microsoft.Extensions.VectorData.VectorSearchFilter' from assembly 'Microsoft.Extensions.VectorData.Abstractions, Version=10.5.0.0'` when searching | **Package version conflict.** `Microsoft.SemanticKernel.Connectors.InMemory` stopped at `1.74.0-preview`, built against VectorData 10.1. SK 1.80.1 brings VectorData 10.5, which removed `VectorSearchFilter`. It compiles fine because the missing type is only used *inside* the connector, so it fails only at runtime. | Replace it with `CommunityToolkit.VectorData.InMemory` (same API, namespace `CommunityToolkit.VectorData.InMemory`). |
| 2 | Build error `SKEXP0010: ... is for evaluation purposes only` | `AddAzureOpenAIEmbeddingGenerator` is marked experimental | `#pragma warning disable SKEXP0010` around that line |

> ✅ **Nice side effect of problem #1:** when the search failed, the model said *"I am currently unable to retrieve the return policy"* and **did not invent one**. The grounding rules in the system prompt worked even when the tool broke.

### Other common errors

| Error | Cause |
|---|---|
| `Missing setting 'AzureOpenAI:EmbeddingDeployment'` | The secret isn't saved (see [Prerequisites](#4-prerequisites)) |
| `HTTP 404 (DeploymentNotFound)` during ingestion | The embedding model isn't deployed in Foundry, or the deployment name is wrong |
| `Ingested 0 chunks` | The `.md` files weren't copied to `bin/`: check the `<None Include=... CopyToOutputDirectory>` entry, or the files have no `## ` headings |
| Dimension mismatch error | `[VectorStoreVector(1536)]` doesn't match the model. `text-embedding-3-small` = 1536, `text-embedding-3-large` = 3072 |

---

## 9. Lessons learned
- **RAG = search + prompt.** The model doesn't learn your documents; we find the relevant text and put it in front of it each time.
- **Retrieval is just a plugin.** Everything from Step 2 (descriptions, automatic function calling, the logging filter) applies directly.
- **Two models, two jobs.** The embedding model finds relevant text; the chat model writes the answer.
- **Chunking matters.** Good chunks (one topic each) give good search results. Structure-based chunking is the simplest when the documents have clear headings.
- **Grounding lives in the system prompt**, and it also protects you when tools fail.
- **Watch package versions.** A package that compiles can still fail at runtime if it was built against an older version of a shared dependency. Check that connector versions match the SK version.

---

## 10. Glossary

| Term | Definition |
|---|---|
| **RAG** | Retrieval-Augmented Generation: retrieve relevant text, then generate an answer from it |
| **Embedding** | A vector of numbers that represents the meaning of a text |
| **Vector store** | A database optimized to store vectors and find the nearest ones |
| **Collection** | A group of records of the same type in a vector store (like a table) |
| **Upsert** | Insert a record, or update it if the key already exists |
| **Top-k** | The number of closest results a search returns (`top: 3`) |
| **Cosine similarity** | A common way to measure how close two vectors are (1 = same direction) |
| **Chunk / chunking** | A piece of a document / the process of splitting documents into pieces |
| **Grounding** | Limiting the model's answer to provided sources |
| **Hallucination** | A plausible-sounding answer that the model invented |
| **`Microsoft.Extensions.VectorData`** | The .NET abstraction layer for vector stores, shared by SK and other libraries |
| **Community Toolkit** | Microsoft / .NET Foundation packages maintained with the community |

---

## 11. Next steps

| Step | Topic | Main concepts |
|---|---|---|
| **4** | Agents | `ChatCompletionAgent`, instructions, multi-agent collaboration (orders agent + policy agent) |
| **5** | Web UI | ASP.NET Core chat page with streaming, tool calls and citations |
| **6** *(optional)* | Production touches | Azure AI Search as a persistent vector store, Entra ID auth, telemetry |
