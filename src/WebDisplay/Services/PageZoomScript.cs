using System;
using System.Globalization;

namespace WebDisplay.Services;

internal static class PageZoomScript
{
    // WinUI 3 WebView2 does not expose the controller's native ZoomFactor.
    // Apply CSS layout zoom only to the top document, so frames scale once.
    internal static string Create(int percent)
    {
        if (percent < 25 || percent > 500) throw new ArgumentOutOfRangeException(nameof(percent));
        return $$"""
            (() => {
                if (window !== window.top) return;
                const percent = {{percent.ToString(CultureInfo.InvariantCulture)}};
                const key = '__webDisplayContentZoom_2_2';
                let state = window[key];
                if (!state && percent === 100) return;
                if (!state) {
                    state = { percent: 100, root: null, active: false, original: '', priority: '' };
                    state.apply = () => {
                        const root = document.documentElement;
                        if (!root) return;
                        if (state.root !== root) {
                            state.rootObserver.disconnect();
                            state.root = root;
                            state.active = false;
                        }
                        // A website may change its own zoom after initial load.
                        // Preserve that new author value before reapplying ours.
                        if (state.active && (root.style.getPropertyValue('zoom') !== state.lastValue || root.style.getPropertyPriority('zoom') !== state.lastPriority)) {
                            state.original = root.style.getPropertyValue('zoom');
                            state.priority = root.style.getPropertyPriority('zoom');
                        }
                        if (state.percent === 100) {
                            state.rootObserver.disconnect();
                            if (state.active) {
                                if (state.original) root.style.setProperty('zoom', state.original, state.priority);
                                else root.style.removeProperty('zoom');
                                state.active = false;
                            }
                            return;
                        }
                        if (!state.active) {
                            state.original = root.style.getPropertyValue('zoom');
                            state.priority = root.style.getPropertyPriority('zoom');
                            state.active = true;
                            state.rootObserver.observe(root, { attributes: true, attributeFilter: ['style'] });
                        }
                        const value = String(state.percent / 100);
                        if (root.style.getPropertyValue('zoom') !== value || root.style.getPropertyPriority('zoom') !== 'important')
                            root.style.setProperty('zoom', value, 'important');
                        state.lastValue = root.style.getPropertyValue('zoom');
                        state.lastPriority = root.style.getPropertyPriority('zoom');
                    };
                    state.rootObserver = new MutationObserver(state.apply);
                    state.documentObserver = new MutationObserver(state.apply);
                    state.documentObserver.observe(document, { childList: true });
                    window[key] = state;
                }
                state.percent = percent;
                state.apply();
            })();
            """;
    }
}
