// 纯布局状态；列表由上到下对应前景到底层，断线不自动替换主画面。
namespace PhoneCam;
public sealed record LayerBox(double X,double Y,double Width,double Height);
public sealed record VideoLayer(Guid Id,LayerBox Box,bool Online,int Rotation=0);
public sealed class Composition
{
    readonly HashSet<Guid> online=new();
    readonly List<Guid> order=new();
    readonly Dictionary<Guid,LayerBox> boxes=new();
    readonly Dictionary<Guid,int> rotations=new();
    public int Rotation(Guid id)=>rotations.GetValueOrDefault(id);
    public void Rotate(Guid id,int delta){if(!online.Contains(id))return;rotations[id]=((Rotation(id)+delta)%360+360)%360;Revision++;}
    public void ResetRotation(Guid id){if(!online.Contains(id))return;rotations.Remove(id);Revision++;}
    public const int MaxLayers=4;
    public int Revision {get;private set;}
    public Guid? Main=>order.Count==0?null:order[^1];
    public bool MainOffline=>Main is Guid id && !online.Contains(id);
    public IReadOnlyList<Guid> Order=>order;
    public bool IsOnline(Guid id)=>online.Contains(id);
    public void Connect(Guid id){online.Add(id);if(Main==null)order.Add(id);Revision++;}
    public void Disconnect(Guid id,bool recovering=false){online.Remove(id);if(Main!=id&&!recovering){order.Remove(id);boxes.Remove(id);}Revision++;}
    // 同一手机重连时更换会话编号，保持层级和位置，不自动加入新画面。
    public void Rebind(Guid oldId,Guid newId){if(rotations.Remove(oldId,out var rotation))rotations[newId]=rotation;int index=order.IndexOf(oldId);if(index>=0)order[index]=newId;if(boxes.Remove(oldId,out var box))boxes[newId]=box;online.Remove(oldId);online.Add(newId);Revision++;}
    public void Reset(){online.Clear();order.Clear();boxes.Clear();rotations.Clear();Revision++;}
    LayerBox DefaultBox(int index)=>new(.65-index*.045,.035+index*.075,.32,.32);
    void Commit(List<Guid> next){order.Clear();order.AddRange(next);for(int i=0;i<order.Count-1;i++)if(!boxes.ContainsKey(order[i]))boxes[order[i]]=DefaultBox(i);Revision++;}
    public bool Promote(Guid id){
        if(!online.Contains(id))return false;
        // 双击只替换底层，原主画面退回未叠加；保留用户已加入的小画面。
        var next=order.Where(x=>x!=id && x!=Main && online.Contains(x)).ToList();
        next.Add(id);Commit(next);return true;
    }
    public bool Stack(Guid source,Guid target){
        if(source==target || !online.Contains(source))return false;
        var next=order.ToList();
        if(!next.Contains(target)){
            if(!online.Contains(target))return false;
            next.RemoveAll(id=>!online.Contains(id));
            next.Remove(source);next.Insert(0,source);next.Add(target);
        }else{next.Remove(source);next.Insert(next.IndexOf(target),source);}
        if(next.Count>MaxLayers)return false;
        Commit(next);return true;
    }
    public void Remove(Guid id){if(id==Main)return;if(order.Remove(id)){boxes.Remove(id);Revision++;}}
    public void Place(Guid id,LayerBox box){
        if(id==Main || !order.Contains(id))return;
        double w=Math.Clamp(box.Width,.08,.9),h=Math.Clamp(box.Height,.08,.9);
        boxes[id]=new(Math.Clamp(box.X,0,1-w),Math.Clamp(box.Y,0,1-h),w,h);Revision++;
    }
    public VideoLayer[] Snapshot()=>order.Select(id=>new VideoLayer(id,id==Main?new(0,0,1,1):boxes[id],online.Contains(id),Rotation(id))).ToArray();
}
