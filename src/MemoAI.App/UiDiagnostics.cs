using System.Diagnostics;
using MemoAI.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MemoAI.App;

public sealed partial class MainWindow
{
    private async Task RunUiSelfTestAsync()
    {
        var result = new Dictionary<string, object>();
        try
        {
            // Invoked only by an explicit development argument with a separate --data-root.
            if (!Environment.GetCommandLineArgs().Any(a => a.StartsWith("--data-root="))) throw new InvalidOperationException("UI 自测必须指定隔离的数据目录。");
            ollamaModels = ["test-translation", "test-summary-a", "test-summary-b"];
            BuildModelStore(); ShowPage(ModelStoreView);
            var backdrop = SystemBackdrop; var rootChildren = Root.Children.ToArray(); var cards = StoreCards.Children.ToArray();
            var buttons = ollamaButtons.ToDictionary(p => p.Key, p => p.Value);
            var watch = Stopwatch.StartNew();
            for (var i = 0; i < 12; i++)
            {
                await UseOllamaModelAsync(i % 2 == 0 ? "test-summary-a" : "test-summary-b", false);
                ApplySettings(); // Re-applying unchanged appearance must preserve the compositor backdrop.
                await Task.Yield();
            }
            result["modelSwitches"] = 12; result["milliseconds"] = watch.Elapsed.TotalMilliseconds;
            result["backdropIdentityPreserved"] = ReferenceEquals(backdrop, SystemBackdrop);
            result["windowTreePreserved"] = rootChildren.SequenceEqual(Root.Children);
            result["modelCardsPreserved"] = cards.SequenceEqual(StoreCards.Children);
            result["buttonsPreserved"] = buttons.All(p => ReferenceEquals(p.Value.Translate, ollamaButtons[p.Key].Translate) && ReferenceEquals(p.Value.Summary, ollamaButtons[p.Key].Summary));
            result["savedModel"] = store.LoadSettings().SummaryModel;
            var passed = result.Where(p => p.Value is bool).All(p => (bool)p.Value);
            result["passed"] = passed;
            if (!passed) throw new InvalidOperationException("UI identity regression");
            Status("UI 自测通过：12 次切换，背景、列表和按钮均保持原对象。");
        }
        catch (Exception ex) { result["passed"] = false; result["error"] = ex.ToString(); }
        await File.WriteAllTextAsync(Path.Combine(paths.DataRoot, "ui-test-results.json"), Json.Write(result));
    }
}
