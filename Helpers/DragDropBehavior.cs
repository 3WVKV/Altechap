using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Altechap.Models;
using Altechap.ViewModels;

using WpfListBox     = System.Windows.Controls.ListBox;
using WpfListBoxItem = System.Windows.Controls.ListBoxItem;
using WpfPoint       = System.Windows.Point;

namespace Altechap.Helpers;

/// <summary>
/// Drag-and-drop LIVE sur ListBox — sans OLE DragDrop.
///
/// OLE DragDrop.DoDragDrop() impose un délai système ~400ms avant d'afficher
/// le curseur ghost. On le remplace par un drag "live" :
///   MouseDown  → mémorise l'item source
///   MouseMove  → dès que le seuil est dépassé, on déplace l'item directement
///               dans la collection à chaque passage sur un item cible
///   MouseUp    → si pas de drag → click (focus)
///
/// Retour visuel pendant le drag :
///   - la carte déplacée passe en <see cref="Character.IsDragging"/> (halo + relief)
///   - une ligne d'insertion turquoise est dessinée dans l'AdornerLayer à
///     l'emplacement où la carte va se poser
///   - auto-scroll quand le curseur approche du haut/bas de la liste
///
/// Résultat : réordonnancement instantané, zéro latence.
/// </summary>
public static class DragDropBehavior
{
    // ── Attached property ─────────────────────────────────────────────────
    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(DragDropBehavior),
            new PropertyMetadata(false, OnChanged));

    public static bool GetEnabled(DependencyObject o) => (bool)o.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject o, bool v) => o.SetValue(EnabledProperty, v);

    // ── Event : click sur un item (pas un drag) ───────────────────────────
    public static readonly RoutedEvent ItemClickedEvent =
        EventManager.RegisterRoutedEvent("ItemClicked", RoutingStrategy.Bubble,
            typeof(RoutedEventHandler<ItemClickedEventArgs>), typeof(DragDropBehavior));

    public static void AddItemClickedHandler(DependencyObject d, RoutedEventHandler<ItemClickedEventArgs> h)
        => (d as UIElement)?.AddHandler(ItemClickedEvent, h);
    public static void RemoveItemClickedHandler(DependencyObject d, RoutedEventHandler<ItemClickedEventArgs> h)
        => (d as UIElement)?.RemoveHandler(ItemClickedEvent, h);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not WpfListBox lb) return;
        if ((bool)e.NewValue)
        {
            lb.PreviewMouseLeftButtonDown += OnDown;
            lb.PreviewMouseLeftButtonUp   += OnUp;
            lb.PreviewMouseMove           += OnMove;
            lb.LostMouseCapture           += OnLostCapture;
        }
        else
        {
            lb.PreviewMouseLeftButtonDown -= OnDown;
            lb.PreviewMouseLeftButtonUp   -= OnUp;
            lb.PreviewMouseMove           -= OnMove;
            lb.LostMouseCapture           -= OnLostCapture;
        }
    }

    private static WpfPoint        _start;
    private static WpfListBoxItem? _srcItem;     // item qu'on drague
    private static Character?      _srcChar;     // datacontext de _srcItem
    private static bool            _dragging;    // vrai dès que seuil dépassé
    private static bool            _dragStarted; // vrai si au moins un move a eu lieu

    private static DropLineAdorner? _line;
    private static AdornerLayer?    _layer;

    private const double AutoScrollZone = 26; // px depuis le bord où le scroll s'enclenche
    private const double AutoScrollStep = 12;

    private static void OnDown(object s, MouseButtonEventArgs e)
    {
        _start       = e.GetPosition(null);
        _dragging    = false;
        _dragStarted = false;
        _srcItem     = null;
        _srcChar     = null;

        if (IsInsideButton((DependencyObject)e.OriginalSource)) return;

        var item = Ancestor<WpfListBoxItem>((DependencyObject)e.OriginalSource);
        if (item?.DataContext is Character ch)
        {
            _srcItem = item;
            _srcChar = ch;
        }
    }

    private static void OnMove(object s, MouseEventArgs e)
    {
        if (s is not WpfListBox lb) return;
        if (e.LeftButton != MouseButtonState.Pressed || _srcItem == null || _srcChar == null)
            return;

        var p  = e.GetPosition(null);
        var dx = Math.Abs(p.X - _start.X);
        var dy = Math.Abs(p.Y - _start.Y);

        // Seuil minimal pour distinguer clic et drag
        if (!_dragging)
        {
            if (dx < SystemParameters.MinimumHorizontalDragDistance &&
                dy < SystemParameters.MinimumVerticalDragDistance) return;
            _dragging    = true;
            _dragStarted = true;
            BeginVisualDrag(lb);
        }

        if (lb.DataContext is not MainViewModel vm) return;

        var posInList = e.GetPosition(lb);
        AutoScroll(lb, e);

        // Trouver l'item sous le curseur
        var hit      = lb.InputHitTest(posInList) as DependencyObject;
        var destItem = Ancestor<WpfListBoxItem>(hit);
        if (destItem != null && destItem != _srcItem && destItem.DataContext is Character destChar)
        {
            int from = vm.Characters.IndexOf(_srcChar);
            int to   = vm.Characters.IndexOf(destChar);
            if (from >= 0 && to >= 0 && from != to)
                vm.MoveCharacter(from, to);
        }

        // La position définitive n'est connue qu'après le passage de layout
        lb.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => UpdateLine(lb)));
    }

    private static void OnUp(object s, MouseButtonEventArgs e)
    {
        if (s is not WpfListBox lb) return;

        bool wasDrag  = _dragStarted;
        var  clicked  = _srcChar;
        bool wasClick = !_dragStarted && _srcItem != null
                        && !IsInsideButton((DependencyObject)e.OriginalSource);

        EndVisualDrag(lb);

        if (wasDrag)
        {
            // Fin du drag : sauvegarder UNE SEULE FOIS (pas à chaque MouseMove)
            if (lb.DataContext is MainViewModel vm) vm.SaveAfterDrag();
        }
        else if (wasClick && clicked != null)
        {
            lb.RaiseEvent(new ItemClickedEventArgs(ItemClickedEvent, lb, clicked));
        }
    }

    /// <summary>Filet de sécurité : Alt+Tab, popup… ne doivent pas laisser un drag "collé".</summary>
    private static void OnLostCapture(object s, MouseEventArgs e)
    {
        if (s is WpfListBox lb && _dragging) EndVisualDrag(lb);
    }

    // ── Retour visuel ─────────────────────────────────────────────────────
    private static void BeginVisualDrag(WpfListBox lb)
    {
        lb.Cursor = Cursors.SizeNS;
        lb.CaptureMouse();
        if (_srcChar != null) _srcChar.IsDragging = true;

        _layer = AdornerLayer.GetAdornerLayer(lb);
        if (_layer != null)
        {
            _line = new DropLineAdorner(lb);
            _layer.Add(_line);
        }
        else Services.Log.Warn("Pas d'AdornerLayer sur la liste — ligne d'insertion indisponible.");

        UpdateLine(lb);
    }

    private static void EndVisualDrag(WpfListBox lb)
    {
        lb.Cursor = null;
        if (lb.IsMouseCaptured) lb.ReleaseMouseCapture();
        if (_srcChar != null) _srcChar.IsDragging = false;

        if (_line != null && _layer != null) _layer.Remove(_line);
        _line  = null;
        _layer = null;

        _srcItem     = null;
        _srcChar     = null;
        _dragging    = false;
        _dragStarted = false;
    }

    /// <summary>Place la ligne d'insertion sur le bord haut de la carte déplacée.</summary>
    private static void UpdateLine(WpfListBox lb)
    {
        if (_line == null || _srcChar == null) return;

        var container = lb.ItemContainerGenerator.ContainerFromItem(_srcChar) as WpfListBoxItem
                        ?? _srcItem;
        if (container == null || !container.IsVisible) { _line.Hide(); return; }

        var top = container.TranslatePoint(new WpfPoint(0, 0), lb).Y;

        // L'AdornerLayer n'est pas clippé par le ScrollViewer : on masque la ligne
        // dès qu'elle sortirait de la zone visible de la liste.
        var sv = Ancestor<ScrollViewer>(lb);
        if (sv != null)
        {
            var yInViewport = container.TranslatePoint(new WpfPoint(0, 0), sv).Y;
            if (yInViewport < -2 || yInViewport > sv.ViewportHeight) { _line.Hide(); return; }
        }

        _line.SetY(Math.Max(1.5, top - 3));
    }

    /// <summary>Fait défiler la liste quand on drague vers le haut/bas de la zone visible.</summary>
    private static void AutoScroll(WpfListBox lb, MouseEventArgs e)
    {
        var sv = Ancestor<ScrollViewer>(lb);
        if (sv == null || sv.ScrollableHeight <= 0) return;

        // Coordonnées dans le viewport, pas dans la liste complète
        var y = e.GetPosition(sv).Y;
        if (y < AutoScrollZone)
            sv.ScrollToVerticalOffset(sv.VerticalOffset - AutoScrollStep);
        else if (y > sv.ViewportHeight - AutoScrollZone)
            sv.ScrollToVerticalOffset(sv.VerticalOffset + AutoScrollStep);
    }

    private static bool IsInsideButton(DependencyObject? element)
    {
        var current = element;
        while (current != null)
        {
            if (current is System.Windows.Controls.Button) return true;
            var parent = VisualTreeHelper.GetParent(current);
            if (parent != null) { current = parent; continue; }
            if (current is FrameworkElement fe && fe.TemplatedParent != null)
            { current = fe.TemplatedParent; continue; }
            break;
        }
        return false;
    }

    private static T? Ancestor<T>(DependencyObject? o) where T : DependencyObject
    {
        while (o != null)
        {
            if (o is T t) return t;
            o = VisualTreeHelper.GetParent(o) ??
                (o as FrameworkElement)?.Parent;
        }
        return null;
    }
}

// ── Ligne d'insertion dessinée par-dessus la liste ───────────────────────
internal sealed class DropLineAdorner : Adorner
{
    private static readonly Brush Accent = CreateAccent();
    private double _y = -100;

    public DropLineAdorner(UIElement adorned) : base(adorned)
    {
        IsHitTestVisible = false;
    }

    public void Hide() => SetY(-100);

    public void SetY(double y)
    {
        if (Math.Abs(_y - y) < 0.5) return;
        _y = y;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_y < 0) return;
        double w = ((FrameworkElement)AdornedElement).ActualWidth;
        if (w <= 12) return;

        dc.DrawRoundedRectangle(Accent, null, new Rect(6, _y - 1.5, w - 12, 3), 1.5, 1.5);
        dc.DrawEllipse(Accent, null, new WpfPoint(6, _y), 4, 4);
        dc.DrawEllipse(Accent, null, new WpfPoint(w - 6, _y), 4, 4);
    }

    private static Brush CreateAccent()
    {
        var b = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x19, 0xB8, 0x8A));
        b.Freeze();
        return b;
    }
}

// ── Event args pour le click sur item ────────────────────────────────────
public class ItemClickedEventArgs : RoutedEventArgs
{
    public Character Character { get; }
    public ItemClickedEventArgs(RoutedEvent re, object src, Character ch) : base(re, src)
        => Character = ch;
}

public delegate void RoutedEventHandler<T>(object sender, T e) where T : RoutedEventArgs;
