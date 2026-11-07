using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace CultCraft
{
    // Render-only props; native animation clocks/attachment slots own their lifetime.
    internal sealed class AnimationPages:IDisposable
    {
        public const float FlightDuration=0.44f;
        private readonly GameObject[] objects=new GameObject[7];
        private readonly Mesh mesh;
        private readonly Material material;
        private string previous="";
        private float previousTime;
        private Vector3 releasePosition;
        private Quaternion releaseRotation;
        private bool released;
        private int lastFrame=-1;
        private BookVisual receivingBook;
        public int GameDrawFrames {get;private set;}
        public bool ThrowObserved {get;private set;}
        public bool CardsObserved {get;private set;}
        public bool HorizontalOrbitObserved {get;private set;}
        public int OpenReceptionFrames {get;private set;}
        public bool ClosedBeforeArrival {get;private set;}
        public AnimationPages(MinecraftAssets assets,Transform parent)
        {
            Texture2D page=assets.Texture("page_26");
            mesh=MinecraftAvatar.GenerateItemMesh(page,"26.3 native animation page",1f,0.015f);
            material=new Material(Shader.Find("Unlit/Texture")){name="CultCraft Minecraft26.3 loose page",mainTexture=page};
            for(int i=0;i<objects.Length;i++)
            {
                GameObject value=new GameObject(i==0?"CultCraft page thrown into book":"CultCraft native orbit page "+i);objects[i]=value;
                value.transform.SetParent(parent,false);value.AddComponent<MeshFilter>().sharedMesh=mesh;
                MeshRenderer renderer=value.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
                int index=i;value.AddComponent<BookRenderProbe>().Observed=delegate(Camera camera){if(camera!=null&&camera.cameraType==CameraType.Game){if(lastFrame!=Time.frameCount){lastFrame=Time.frameCount;GameDrawFrames++;}if(index>0)CardsObserved=true;else if(released){ThrowObserved=true;if(receivingBook!=null&&receivingBook.Visible&&receivingBook.Openness>=0.95f)OpenReceptionFrames++;else ClosedBeforeArrival=true;}}};
                value.SetActive(false);
            }
        }
        public void Tick(bool visible,bool firstPerson,string animation,float time,bool nativeItem,float throwAt,Transform root,BookVisual bookVisual,Vector3 palm,Quaternion wrist,bool[] cardVisible,Vector2[] cards,float[] angles,Vector2 hip,float scale)
        {
            receivingBook=bookVisual;Transform book=bookVisual.Root;
            if(previous!=animation||time+0.01f<previousTime){released=false;previous=animation;}
            previousTime=time;
            bool throwing=animation.StartsWith("find-item/",StringComparison.Ordinal)&&throwAt>=0f;
            float flight=time-throwAt;
            const float duration=FlightDuration,pageSize=0.27f;
            bool page=visible&&!firstPerson&&throwing&&((nativeItem&&flight<0f)||(flight>=0f&&flight<duration));
            objects[0].SetActive(page);
            if(page)
            {
                Transform p=objects[0].transform;
                Quaternion heldRotation=root.rotation*Quaternion.Euler(20f,15f,-20f);
                // Creator's final preference: original size, readable levitation
                // above the hand rather than an edge grip hidden by its cuboid.
                Vector3 heldPosition=palm+root.TransformVector(Vector3.up*0.14f);
                if(flight<0f){p.SetPositionAndRotation(heldPosition,heldRotation);releasePosition=root.InverseTransformPoint(heldPosition);releaseRotation=Quaternion.Inverse(root.rotation)*heldRotation;}
                else
                {
                    if(!released){released=true;releasePosition=root.InverseTransformPoint(heldPosition);releaseRotation=Quaternion.Inverse(root.rotation)*heldRotation;}
                    float raw=Mathf.Clamp01(flight/duration),t=Mathf.SmoothStep(0f,1f,raw);
                    Vector3 target=book!=null?root.InverseTransformPoint(book.position):releasePosition;
                    p.localPosition=Vector3.Lerp(releasePosition,target,t)+Vector3.up*(Mathf.Sin(raw*Mathf.PI)*0.09f);
                    p.localRotation=Quaternion.Slerp(releaseRotation,book!=null?Quaternion.Inverse(root.rotation)*book.rotation:releaseRotation,t);
                }
                p.localScale=Vector3.one*pageSize;
            }
            Vector2 centre=Vector2.zero;for(int i=0;i<6;i++)centre+=cards[i];centre/=6f;
            int orbitCount=0;float minX=Single.MaxValue,maxX=Single.MinValue,minZ=Single.MaxValue,maxZ=Single.MinValue;
            for(int i=0;i<6;i++)
            {
                bool show=visible&&!firstPerson&&animation.StartsWith("cards/",StringComparison.Ordinal)&&cardVisible[i];
                GameObject value=objects[i+1];value.SetActive(show);if(!show)continue;
                Vector2 native=(cards[i]-centre)*scale*0.8f;
                // Spine's screen-space circular path becomes the avatar's XZ
                // ground plane. Height stays fixed instead of a vertical clock face.
                value.transform.localPosition=new Vector3(native.x,MinecraftAvatar.BodyHeight*0.7f,native.y);
                Vector3 outward=new Vector3(native.x,0f,native.y);
                value.transform.localRotation=outward.sqrMagnitude>0.001f?Quaternion.LookRotation(outward,Vector3.up):Quaternion.identity;
                value.transform.localScale=Vector3.one*0.38f;
                orbitCount++;minX=Mathf.Min(minX,native.x);maxX=Mathf.Max(maxX,native.x);minZ=Mathf.Min(minZ,native.y);maxZ=Mathf.Max(maxZ,native.y);
            }
            if(orbitCount>=4&&maxX-minX>0.2f&&maxZ-minZ>0.2f)HorizontalOrbitObserved=true;
        }
        public void Dispose(){foreach(GameObject value in objects)UnityEngine.Object.Destroy(value);UnityEngine.Object.Destroy(mesh);UnityEngine.Object.Destroy(material);}
    }
}
