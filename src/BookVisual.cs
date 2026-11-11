using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CultCraft
{
    // Vanilla 1.21 BookModel.createBodyLayer/setupAnim, using the owner's 64x32 texture.
    public sealed class BookVisual : IDisposable
    {
        private readonly GameObject root;
        private readonly Transform leftLid, rightLid, leftPages, rightPages, flip1, flip2;
        private readonly List<Mesh> meshes = new List<Mesh>();
        private readonly List<MeshRenderer> renderers = new List<MeshRenderer>();
        private readonly Material material;
        private readonly NativeSermonGlyphs.BookParticles particles;
        private int lastDrawFrame = -1;
        private bool disposed;
        public Transform Root { get { return root != null ? root.transform : null; } }
        public bool Visible { get { return !disposed && root != null && root.activeInHierarchy; } }
        public int PartCount { get { return renderers.Count; } }
        public float Openness { get; private set; }
        public int GameDrawFrames { get; private set; }
        public float NativeTime { get; private set; }
        public int ParticleGameDrawFrames { get { return particles==null?0:particles.GameDrawFrames; } }
        public bool VolumetricPages { get { return leftPages.GetComponent<MeshFilter>().sharedMesh.bounds.size.z > 0.05f; } }

        public BookVisual(MinecraftAssets assets, Transform parent, bool firstPerson)
        {
            root = new GameObject("CultCraft authentic seven-part enchanting book");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = firstPerson ? new Vector3(0f, Spec.Visual("book_view_y").Value, Spec.Visual("book_view_z").Value) : new Vector3(0f, Spec.Visual("book_world_y").Value, Spec.Visual("book_world_z").Value);
            root.transform.localScale = Vector3.one * Spec.Visual("book_size").Value;
            Shader shader = Shader.Find("Unlit/Texture");
            Texture2D texture = assets.Texture("enchanting_table_book");
            if (texture == null || shader == null || !shader.isSupported) throw new InvalidOperationException("Installed Minecraft enchanting-book depth material unavailable.");
            material = new Material(shader) { name = "CultCraft original Minecraft enchanting book atlas", mainTexture = texture };
            leftLid = Part("left_lid", new Vector3(-6f,-5f,-0.005f), new Vector3(6f,10f,0.005f), 0f,0f,new Vector3(0f,0f,-1f));
            rightLid = Part("right_lid", new Vector3(0f,-5f,-0.005f), new Vector3(6f,10f,0.005f),16f,0f,new Vector3(0f,0f,1f));
            Transform seam = Part("seam",new Vector3(-1f,-5f,0f),new Vector3(2f,10f,0.005f),12f,0f,Vector3.zero);
            seam.localRotation = Quaternion.AngleAxis(-90f,Vector3.up);
            leftPages = Part("left_pages",new Vector3(0f,-4f,-0.99f),new Vector3(5f,8f,1f),0f,10f,Vector3.zero);
            rightPages = Part("right_pages",new Vector3(0f,-4f,-0.01f),new Vector3(5f,8f,1f),12f,10f,Vector3.zero);
            flip1 = Part("flip_page1",new Vector3(0f,-4f,0f),new Vector3(5f,8f,0.005f),24f,10f,Vector3.zero);
            flip2 = Part("flip_page2",new Vector3(0f,-4f,0f),new Vector3(5f,8f,0.005f),24f,10f,Vector3.zero);
            particles=NativeSermonGlyphs.CreateBookParticles(root.transform);
            root.SetActive(false);
        }

        private static Vector3 Convert(Vector3 v) { return new Vector3(v.x,-v.y,-v.z)/16f; }

        private Transform Part(string name,Vector3 min,Vector3 size,float u,float v,Vector3 pivot)
        {
            Vector3 max=min+size;float w=size.x,h=size.y,d=size.z;
            var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
            Face(vertices,uv,triangles,new[]{new Vector3(max.x,min.y,min.z),min,new Vector3(min.x,max.y,min.z),new Vector3(max.x,max.y,min.z)},new Rect(u+d,v+d,w,h));
            Face(vertices,uv,triangles,new[]{new Vector3(min.x,min.y,max.z),new Vector3(max.x,min.y,max.z),max,new Vector3(min.x,max.y,max.z)},new Rect(u+d+w+d,v+d,w,h));
            Face(vertices,uv,triangles,new[]{min,new Vector3(min.x,min.y,max.z),new Vector3(min.x,max.y,max.z),new Vector3(min.x,max.y,min.z)},new Rect(u,v+d,d,h));
            Face(vertices,uv,triangles,new[]{new Vector3(max.x,min.y,max.z),new Vector3(max.x,min.y,min.z),new Vector3(max.x,max.y,min.z),max},new Rect(u+d+w,v+d,d,h));
            Face(vertices,uv,triangles,new[]{new Vector3(min.x,max.y,max.z),max,new Vector3(max.x,max.y,min.z),new Vector3(min.x,max.y,min.z)},new Rect(u+d,v,w,d));
            Face(vertices,uv,triangles,new[]{min,new Vector3(max.x,min.y,min.z),new Vector3(max.x,min.y,max.z),new Vector3(min.x,min.y,max.z)},new Rect(u+d+w,v,w,d));
            var mesh=new Mesh {name="CultCraft vanilla BookModel "+name};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();meshes.Add(mesh);
            var obj=new GameObject(name);obj.transform.SetParent(root.transform,false);obj.transform.localPosition=Convert(pivot);
            obj.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=obj.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;renderers.Add(renderer);
            obj.AddComponent<BookRenderProbe>().Observed=ObservedDraw;
            return obj.transform;
        }

        private static void Face(List<Vector3> vertices,List<Vector2> uv,List<int> triangles,Vector3[] corners,Rect rect)
        {
            int start=vertices.Count;foreach(Vector3 corner in corners)vertices.Add(Convert(corner));
            float u0=rect.x/64f,u1=(rect.x+rect.width)/64f,v0=1f-rect.y/32f,v1=1f-(rect.y+rect.height)/32f;
            uv.Add(new Vector2(u0,v0));uv.Add(new Vector2(u1,v0));uv.Add(new Vector2(u1,v1));uv.Add(new Vector2(u0,v1));
            foreach(int n in new[]{0,1,2,0,2,3})triangles.Add(start+n);
        }

        private void ObservedDraw(Camera camera)
        {
            if (!Visible || camera == null || camera.cameraType != CameraType.Game || lastDrawFrame == Time.frameCount) return;
            lastDrawFrame=Time.frameCount;GameDrawFrames++;
        }

        public void SetSorting(int layer,int order)
        { foreach(MeshRenderer renderer in renderers){renderer.sortingLayerID=layer;renderer.sortingOrder=order;}if(particles!=null)particles.SetSorting(layer,order); }
        public void SetWorldPose(Vector3 position,Quaternion rotation)
        { root.transform.SetPositionAndRotation(position,rotation); }
        public void SetLocalPose(Vector3 position,Quaternion rotation)
        { root.transform.localPosition=position;root.transform.localRotation=rotation; }

        public void Tick(bool visible,float open,float nativeTime,float page1,float page2)
        {
            if(disposed)return;root.SetActive(visible);Openness=visible?Mathf.Clamp01(open):0f;NativeTime=nativeTime;
            if(particles!=null)particles.Tick(visible&&Openness>0.35f,nativeTime,Openness);
            if(!visible)return;
            // Source setupAnim: the native track drives age/open/page inputs; no independent clock.
            float angle=(Mathf.Sin(nativeTime*20f*0.02f)*0.1f+1.25f)*Openness;
            leftLid.localRotation=Quaternion.AngleAxis(-(Mathf.PI+angle)*Mathf.Rad2Deg,Vector3.up);
            rightLid.localRotation=Quaternion.AngleAxis(angle*Mathf.Rad2Deg,Vector3.up);
            leftPages.localRotation=Quaternion.AngleAxis(-angle*Mathf.Rad2Deg,Vector3.up);
            rightPages.localRotation=Quaternion.AngleAxis(angle*Mathf.Rad2Deg,Vector3.up);
            flip1.localRotation=Quaternion.AngleAxis(-(angle-angle*2f*Mathf.Clamp01(page1))*Mathf.Rad2Deg,Vector3.up);
            flip2.localRotation=Quaternion.AngleAxis(-(angle-angle*2f*Mathf.Clamp01(page2))*Mathf.Rad2Deg,Vector3.up);
            Vector3 pagePosition=Vector3.right*(Mathf.Sin(angle)/16f);
            leftPages.localPosition=rightPages.localPosition=flip1.localPosition=flip2.localPosition=pagePosition;
        }

        public void Dispose()
        { if(disposed)return;disposed=true;if(particles!=null)particles.Dispose();UnityEngine.Object.Destroy(root);foreach(Mesh mesh in meshes)UnityEngine.Object.Destroy(mesh);UnityEngine.Object.Destroy(material); }
    }

    // Unity calls this only when an actual camera submits an enabled part for rendering.
    public sealed class BookRenderProbe : MonoBehaviour
    {
        public Action<Camera> Observed;
        private void OnWillRenderObject(){if(Observed!=null)Observed(Camera.current);}
    }
}
