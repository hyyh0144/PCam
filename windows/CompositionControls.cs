// 拖放层级、双击主画面、预览拖移和等比缩放；编辑元素不交给编码器。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
namespace PhoneCam;
public sealed record DeviceEntry(Peer Peer,string Label);
public partial class MainWindow
{
    Point dragStart,pictureStart;
    Guid? dragDevice,editing;
    LayerBox? startBox;
    bool resizing;
    bool dropped;
    ListBoxItem? dropItem;
    const string DragFormat="PCam.PhoneLayer";
    void SyncDevices(){
        Guid? keep=(Devices.SelectedItem as DeviceEntry)?.Peer.Id;
        foreach(var id in known.Keys.Where(id=>!composition.IsOnline(id) && !composition.Order.Contains(id) && !recovering.ContainsKey(id)).ToArray())known.Remove(id);
        syncing=true;devices.Clear();
        var ids=known.Keys.Where(id=>!composition.Order.Contains(id)).Concat(composition.Order).ToArray();
        foreach(var id in ids)if(known.TryGetValue(id,out var peer)){
            string role=id==composition.Main?(composition.IsOnline(id)?"主画面":"主画面 · 已断开"):composition.Order.Contains(id)?"叠加画面":"未叠加";
            if(!composition.IsOnline(id) && id!=composition.Main)role="正在重连";
            devices.Add(new(peer,peer.Name+" · "+role));
        }
        Devices.SelectedItem=devices.FirstOrDefault(d=>d.Peer.Id==keep)??devices.FirstOrDefault(d=>composition.IsOnline(d.Peer.Id));
        syncing=false;UpdateSelectedDevice();
    }
    void UpdateCompositionHint(){
        CompositionHint.Text=composition.MainOffline?"主画面已断开 · 双击手机重新设为主画面":"连接手机后，画面会显示在这里";
        CompositionHint.Visibility=composition.Main==null || composition.MainOffline?Visibility.Visible:Visibility.Collapsed;
        Status.Text=composition.MainOffline?"主画面已断开，其他叠加画面继续显示":$"已连接 {known.Keys.Count(composition.IsOnline)} 部手机 · 最下方为主画面";
        DrawHandles();
    }
    DeviceEntry? EntryAt(Point point){
        // 直接按可见行边界定位，不依赖模板内部命中元素的 DataContext。
        if(!new Rect(0,0,Devices.ActualWidth,Devices.ActualHeight).Contains(point))return null;
        foreach(var entry in devices){
            if(Devices.ItemContainerGenerator.ContainerFromItem(entry) is not ListBoxItem item)continue;
            var origin=item.TranslatePoint(new Point(),Devices);
            if(new Rect(origin,new Size(item.ActualWidth,item.ActualHeight)).Contains(point))return entry;
        }
        return null;
    }
    void DeviceDown(object sender,MouseButtonEventArgs e){dragStart=e.GetPosition(Devices);dragDevice=EntryAt(dragStart)?.Peer.Id;}
    void DeviceMove(object sender,MouseEventArgs e){
        if(e.LeftButton!=MouseButtonState.Pressed || dragDevice==null)return;
        var delta=e.GetPosition(Devices)-dragStart;if(Math.Abs(delta.X)<SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y)<SystemParameters.MinimumVerticalDragDistance)return;
        var id=dragDevice.Value;dragDevice=null;if(!composition.IsOnline(id))return;
        Mouse.Capture(null); // 释放列表选中时的鼠标捕获，交给系统拖放。
        dropped=false;try{DragDrop.DoDragDrop(Devices,new DataObject(DragFormat,id.ToString()),DragDropEffects.Move);}finally{ClearDrop();if(!dropped)UpdateCompositionHint();}
    }
    void ClearDrop(){if(dropItem!=null){dropItem.ClearValue(Control.BackgroundProperty);dropItem=null;}}
    void DeviceDragLeave(object sender,DragEventArgs e){ClearDrop();}
    void DeviceDragOver(object sender,DragEventArgs e){
        e.Handled=true;e.Effects=DragDropEffects.None;ClearDrop();
        if(!e.Data.GetDataPresent(DragFormat) || !Guid.TryParse(e.Data.GetData(DragFormat) as string,out var source) || !known.TryGetValue(source,out var sourcePeer) || !composition.IsOnline(source))return;
        var target=EntryAt(e.GetPosition(Devices));if(target==null || source==target.Peer.Id)return;
        dropItem=Devices.ItemContainerGenerator.ContainerFromItem(target) as ListBoxItem;
        dropItem?.SetValue(Control.BackgroundProperty,Brushes.LightSkyBlue);e.Effects=DragDropEffects.Move;
        Status.Text=$"将 {sourcePeer.Name} 叠加到 {target.Peer.Name} 上方";
        int index=devices.IndexOf(target);double y=e.GetPosition(Devices).Y;
        if(y<16 && index>0)Devices.ScrollIntoView(devices[index-1]);
        else if(y>Devices.ActualHeight-16 && index<devices.Count-1)Devices.ScrollIntoView(devices[index+1]);
    }
    void DeviceDrop(object sender,DragEventArgs e){
        e.Handled=true;dropped=true;ClearDrop();var target=EntryAt(e.GetPosition(Devices));
        if(target==null || !Guid.TryParse(e.Data.GetData(DragFormat) as string,out var source))return;
        if(composition.Stack(source,target.Peer.Id)){SyncDevices();UpdateCompositionHint();Render();}
        else Status.Text="最多叠加 3 个小画面；可右键移出一个画面后重试";
    }
    void DeviceDoubleClick(object sender,MouseButtonEventArgs e){
        var entry=EntryAt(e.GetPosition(Devices));if(entry==null)return;
        dragDevice=null;
        if(composition.Promote(entry.Peer.Id)){SyncDevices();UpdateCompositionHint();Render();}
        else Status.Text=composition.IsOnline(entry.Peer.Id)?"已满 4 层，请先右键移出一个小画面":"这部手机已断开，请双击在线手机";
        e.Handled=true;
    }
    void DeviceRightClick(object sender,MouseButtonEventArgs e){
        var entry=EntryAt(e.GetPosition(Devices));if(entry==null)return;Devices.SelectedItem=entry;
        var menu=new ContextMenu();var remove=new MenuItem{Header="移出叠加",IsEnabled=composition.Order.Contains(entry.Peer.Id) && composition.Main!=entry.Peer.Id};
        remove.Click+=(_,_)=>{composition.Remove(entry.Peer.Id);SyncDevices();UpdateCompositionHint();Render();};menu.Items.Add(remove);menu.IsOpen=true;e.Handled=true;
    }
    Rect CanvasRect()=>composed==null?Rect.Empty:FrameComposer.Fit(composed.Image.PixelWidth,composed.Image.PixelHeight,new Rect(0,0,PreviewArea.ActualWidth,PreviewArea.ActualHeight));
    Rect ScreenRect(Rect rect){var canvas=CanvasRect();return new(canvas.X+rect.X/composed!.Image.PixelWidth*canvas.Width,canvas.Y+rect.Y/composed.Image.PixelHeight*canvas.Height,rect.Width/composed.Image.PixelWidth*canvas.Width,rect.Height/composed.Image.PixelHeight*canvas.Height);}
    void DrawHandles(){
        if(EditLayer==null)return;EditLayer.Children.Clear();
        var id=editing??selected?.Id;if(composed==null || id==null || !composed.Bounds.TryGetValue(id.Value,out var bounds))return;
        var rect=ScreenRect(bounds);if(rect.IsEmpty || rect.Width<=0)return;
        var outline=new Rectangle{Width=rect.Width,Height=rect.Height,Stroke=Brushes.DeepSkyBlue,StrokeThickness=2,IsHitTestVisible=false};Canvas.SetLeft(outline,rect.X);Canvas.SetTop(outline,rect.Y);EditLayer.Children.Add(outline);
        var handle=new Rectangle{Width=12,Height=12,Fill=Brushes.DeepSkyBlue,IsHitTestVisible=false};Canvas.SetLeft(handle,rect.Right-6);Canvas.SetTop(handle,rect.Bottom-6);EditLayer.Children.Add(handle);
    }
    void PictureDown(object sender,MouseButtonEventArgs e){
        if(composed==null)return;var point=e.GetPosition(EditLayer);
        foreach(var layer in composition.Snapshot().Take(Math.Max(0,composition.Order.Count-1))){
            if(!composed.Bounds.TryGetValue(layer.Id,out var bound))continue;var rect=ScreenRect(bound);var hit=rect;hit.Inflate(8,8);if(!hit.Contains(point))continue;
            editing=layer.Id;pictureStart=point;startBox=layer.Box;resizing=Math.Abs(point.X-rect.Right)<14 && Math.Abs(point.Y-rect.Bottom)<14;
            Devices.SelectedItem=devices.FirstOrDefault(d=>d.Peer.Id==layer.Id);EditLayer.CaptureMouse();DrawHandles();e.Handled=true;break;
        }
    }
    void PictureMove(object sender,MouseEventArgs e){
        if(editing==null || startBox==null || e.LeftButton!=MouseButtonState.Pressed)return;
        var canvas=CanvasRect();if(canvas.Width<=0 || canvas.Height<=0)return;var delta=e.GetPosition(EditLayer)-pictureStart;var b=startBox;
        if(resizing){double scale=Math.Clamp(1+delta.X/(b.Width*canvas.Width),Math.Max(.08/b.Width,.08/b.Height),Math.Min(.9/b.Width,.9/b.Height));composition.Place(editing.Value,new(b.X,b.Y,b.Width*scale,b.Height*scale));}
        else composition.Place(editing.Value,new(b.X+delta.X/canvas.Width,b.Y+delta.Y/canvas.Height,b.Width,b.Height));
        Render();e.Handled=true;
    }
    void PictureUp(object sender,MouseButtonEventArgs e){editing=null;startBox=null;EditLayer.ReleaseMouseCapture();DrawHandles();}
    void PictureCaptureLost(object sender,MouseEventArgs e){editing=null;startBox=null;DrawHandles();}
}
