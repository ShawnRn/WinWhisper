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
            await Task.Delay(100); // Let the real WinUI layout establish the scroll extent.
            StoreScroll.ChangeView(null, StoreScroll.ScrollableHeight, null, true);
            ollamaButtons["test-summary-b"].Summary.Focus(FocusState.Programmatic);
            await Task.Delay(100);
            var scrollOffset = StoreScroll.VerticalOffset;
            var backdrop = SystemBackdrop; var rootChildren = Root.Children.ToArray(); var cards = StoreCards.Children.ToArray();
            var buttons = ollamaButtons.ToDictionary(p => p.Key, p => p.Value);
            var watch = Stopwatch.StartNew();
            for (var i = 0; i < 12; i++)
            {
                await UseOllamaModelAsync(i % 2 == 0 ? "test-summary-a" : "test-summary-b", false);
                await UseOllamaModelAsync(i % 2 == 0 ? "test-summary-a" : "test-translation", true);
                ApplySettings(); // Re-applying unchanged appearance must preserve the compositor backdrop.
                await Task.Yield();
            }
            await Task.Delay(100);
            result["modelSwitches"] = 24; result["milliseconds"] = watch.Elapsed.TotalMilliseconds;
            result["backdropIdentityPreserved"] = ReferenceEquals(backdrop, SystemBackdrop);
            result["windowTreePreserved"] = rootChildren.SequenceEqual(Root.Children);
            result["modelCardsPreserved"] = cards.SequenceEqual(StoreCards.Children);
            result["buttonsPreserved"] = buttons.All(p => ReferenceEquals(p.Value.Translate, ollamaButtons[p.Key].Translate) && ReferenceEquals(p.Value.Summary, ollamaButtons[p.Key].Summary));
            result["scrollOffsetBefore"] = scrollOffset; result["scrollOffsetAfter"] = StoreScroll.VerticalOffset;
            result["scrollPositionPreserved"] = scrollOffset > 0 && Math.Abs(scrollOffset - StoreScroll.VerticalOffset) < 1;
            result["focusPreserved"] = ollamaButtons["test-summary-b"].Summary.FocusState != FocusState.Unfocused;
            result["translationSaved"] = store.LoadSettings().TranslationModel == "test-translation";
            result["summarySaved"] = store.LoadSettings().SummaryModel == "test-summary-b";
            result["savedModel"] = store.LoadSettings().SummaryModel;
            var passed = result.Where(p => p.Value is bool).All(p => (bool)p.Value);
            result["passed"] = passed;
            if (!passed) throw new InvalidOperationException("UI identity regression");
            Status("UI 自测通过：24 次切换，背景、列表、滚动位置和焦点均保持稳定。");
        }
        catch (Exception ex) { result["passed"] = false; result["error"] = ex.ToString(); }
        await File.WriteAllTextAsync(Path.Combine(paths.DataRoot, "ui-test-results.json"), Json.Write(result));
        if (Environment.GetCommandLineArgs().Contains("--exit-after-test")) Close();
    }
}
