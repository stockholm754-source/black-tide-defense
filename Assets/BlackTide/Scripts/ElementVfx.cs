using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlackTide
{
    // Finite visual envelopes only: no colliders, damage, status changes or saved settings.
    // One mesh/material per envelope, bounded geometry, GPU flame motion, reused vertex buffers.
    public sealed class ElementVfx : MonoBehaviour
    {
        public enum Shape { Fire, Shield, Sound, Vortex, Sweep, Ice, SoundPulse, Lightning }
        Shape shape;
        float radius, duration, age;
        Damageable follow;
        Mesh mesh;
        Material material;
        readonly List<Vector3> vertices=new List<Vector3>(512);
        readonly List<Vector2> uv=new List<Vector2>(512);
        readonly List<Color> colors=new List<Color>(512);
        readonly List<int> triangles=new List<int>(1536);

        public static ElementVfx Spawn(Shape kind,Vector3 p,Vector3 dir,float r,float dur,Color color,Damageable target=null)
        {
            var go=new GameObject("Element_"+kind);
            go.transform.position=p;
            dir.y=0; if(dir.sqrMagnitude>.001f) go.transform.rotation=Quaternion.LookRotation(dir);
            var fx=go.AddComponent<ElementVfx>(); fx.shape=kind; fx.radius=Mathf.Max(.1f,r);
            fx.duration=Mathf.Max(.05f,dur); fx.follow=target;
            fx.mesh=new Mesh { name="Element_"+kind }; fx.mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh=fx.mesh;
            var renderer=go.AddComponent<MeshRenderer>(); renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
            fx.material=new Material(Shader.Find("BlackTide/Element"));
            fx.material.SetColor("_Color",color); fx.material.SetFloat("_Kind",(float)kind);
            fx.material.SetFloat("_Seed",Random.Range(0f,10f));
            renderer.sharedMaterial=fx.material;
            fx.Build(); fx.Upload(); return fx;
        }
        void Vertex(Vector3 p,Vector2 st,float a=1)
        { vertices.Add(p); uv.Add(st); colors.Add(new Color(1,1,1,a)); }
        void Strip(Vector3 a,Vector3 b,Vector3 c,Vector3 d,float u0,float u1,float alpha=1)
        {
            int n=vertices.Count;
            Vertex(a,new Vector2(u0,0),alpha); Vertex(b,new Vector2(u0,1),alpha);
            Vertex(c,new Vector2(u1,0),alpha); Vertex(d,new Vector2(u1,1),alpha);
            triangles.Add(n); triangles.Add(n+2); triangles.Add(n+1);
            triangles.Add(n+1); triangles.Add(n+2); triangles.Add(n+3);
        }
        void ResetBuffers() { vertices.Clear(); uv.Clear(); colors.Clear(); triangles.Clear(); }
        void Upload()
        {
            mesh.Clear(); mesh.SetVertices(vertices); mesh.SetUVs(0,uv); mesh.SetColors(colors); mesh.SetTriangles(triangles,0); mesh.RecalculateBounds();
            // Vertex shader sways flames sideways. Preserve the displaced geometry in culling bounds.
            if(shape==Shape.Fire) { var b=mesh.bounds; b.Expand(.8f); mesh.bounds=b; }
        }
        void Build()
        {
            ResetBuffers();
            if(shape==Shape.Fire) BuildFire();
            else if(shape==Shape.Shield) BuildShield();
            else if(shape==Shape.Sound) BuildSound();
            else if(shape==Shape.Ice) BuildIce();
            else if(shape==Shape.SoundPulse) BuildSoundPulse();
            else if(shape==Shape.Lightning) return;
            else BuildWind();
        }
        void BuildFire()
        {
            int tongues=Mathf.Clamp(Mathf.RoundToInt(radius*4),8,18);
            for(int i=0;i<tongues;i++)
            {
                float a=Random.value*Mathf.PI*2, rr=radius*Mathf.Sqrt(Random.value)*.83f;
                var p=new Vector3(Mathf.Sin(a)*rr,.06f,Mathf.Cos(a)*rr);
                float height=Random.Range(.8f,1.65f), width=Random.Range(.24f,.5f);
                // Two curved, tapering ribbons intersect in world space, rather than facing the camera.
                for(int side=0;side<2;side++)
                {
                    var right=Quaternion.Euler(0,a*Mathf.Rad2Deg+side*90,0)*Vector3.right;
                    for(int j=0;j<6;j++)
                    {
                        float v0=j/6f,v1=(j+1)/6f;
                        float w0=width*(1-v0),w1=width*(1-v1);
                        var c0=p+Vector3.up*(height*v0)+right*(Mathf.Sin(v0*3+i)*v0*.17f);
                        var c1=p+Vector3.up*(height*v1)+right*(Mathf.Sin(v1*3+i)*v1*.17f);
                        // Fire UV: horizontal x, vertical y.
                        int n=vertices.Count;
                        Vertex(c0-right*w0,new Vector2(0,v0)); Vertex(c0+right*w0,new Vector2(1,v0));
                        Vertex(c1-right*w1,new Vector2(0,v1)); Vertex(c1+right*w1,new Vector2(1,v1));
                        triangles.AddRange(new[]{n,n+2,n+1,n+1,n+2,n+3});
                    }
                }
            }
        }
        void BuildShield()
        {
            float h=follow ? follow.height*1.22f : 2.5f;
            var hex=new[]{new Vector2(0,.5f),new Vector2(.5f,.25f),new Vector2(.5f,-.25f),new Vector2(0,-.5f),new Vector2(-.5f,-.25f),new Vector2(-.5f,.25f)};
            for(int panel=0;panel<6;panel++)
            {
                float a=panel*Mathf.PI/3;
                var normal=new Vector3(Mathf.Sin(a),0,Mathf.Cos(a));
                var right=new Vector3(normal.z,0,-normal.x);
                int center=vertices.Count; Vertex(normal*radius,new Vector2(.5f,.5f));
                foreach(var v in hex) Vertex(normal*radius+right*(v.x*radius*1.05f)+Vector3.up*(v.y*h),v+Vector2.one*.5f);
                for(int i=0;i<6;i++) { triangles.Add(center); triangles.Add(center+1+i); triangles.Add(center+1+(i+1)%6); }
            }
        }
        void BuildSound()
        {
            for(int wave=0;wave<3;wave++)
            {
                float k=(age/duration-wave*.16f)/.65f;
                if(k<0 || k>1) continue;
                float r=Mathf.Lerp(.35f,radius,k), alpha=1-Mathf.SmoothStep(0,1,(k-.65f)/.35f);
                for(int j=0;j<32;j++)
                {
                    float u0=j/32f,u1=(j+1)/32f;
                    float a0=Mathf.Lerp(-55,55,u0)*Mathf.Deg2Rad,a1=Mathf.Lerp(-55,55,u1)*Mathf.Deg2Rad;
                    var n0=new Vector3(Mathf.Sin(a0),0,Mathf.Cos(a0)); var n1=new Vector3(Mathf.Sin(a1),0,Mathf.Cos(a1));
                    float y0=.25f+Mathf.Sin(u0*Mathf.PI)*.35f, y1=.25f+Mathf.Sin(u1*Mathf.PI)*.35f;
                    Strip(n0*(r-.06f)+Vector3.up*y0,n0*(r+.06f)+Vector3.up*y0,n1*(r-.06f)+Vector3.up*y1,n1*(r+.06f)+Vector3.up*y1,u0,u1,alpha);
                }
            }
        }
        void BuildWind()
        {
            bool sweep=shape==Shape.Sweep;
            float front=Mathf.Lerp(1.2f,radius,Mathf.Clamp01(age/duration));
            for(int strand=0;strand<5;strand++)
                for(int j=0;j<24;j++)
                {
                    float u0=j/24f,u1=(j+1)/24f;
                    // Short inward curls, never overlapping full spiral turns that read as stacked rings.
                    float a0=strand*Mathf.PI*.4f+u0*(sweep?.7f:1.25f)-age*2;
                    float a1=strand*Mathf.PI*.4f+u1*(sweep?.7f:1.25f)-age*2;
                    float r0=sweep ? front-.4f*(1-u0) : radius*(1-.87f*u0);
                    float r1=sweep ? front-.4f*(1-u1) : radius*(1-.87f*u1);
                    var n0=new Vector3(Mathf.Sin(a0),0,Mathf.Cos(a0)); var n1=new Vector3(Mathf.Sin(a1),0,Mathf.Cos(a1));
                    float y0=.12f+Mathf.Sin(u0*Mathf.PI)*.65f,y1=.12f+Mathf.Sin(u1*Mathf.PI)*.65f;
                    float alpha=Mathf.Sin((u0+.01f)*Mathf.PI);
                    Strip(n0*r0+Vector3.up*y0,n0*r0+Vector3.up*(y0+.08f),n1*r1+Vector3.up*y1,n1*r1+Vector3.up*(y1+.08f),u0,u1,alpha);
                }
        }
        void BuildIce()
        {
            for(int cluster=0;cluster<12;cluster++)
            {
                float a=Random.value*Mathf.PI*2, r=radius*Random.Range(.5f,.95f);
                var p=new Vector3(Mathf.Sin(a)*r,.04f,Mathf.Cos(a)*r);
                float h=Random.Range(.35f,.9f),w=Random.Range(.08f,.17f);
                for(int side=0;side<4;side++)
                {
                    int n=vertices.Count; float s=side*Mathf.PI*.5f;
                    Vertex(p+new Vector3(Mathf.Sin(s)*w,0,Mathf.Cos(s)*w),Vector2.zero,.45f);
                    Vertex(p+new Vector3(Mathf.Sin(s+Mathf.PI*.5f)*w,0,Mathf.Cos(s+Mathf.PI*.5f)*w),Vector2.right,.45f);
                    Vertex(p+Vector3.up*h+new Vector3(w,0,0),new Vector2(.5f,1));
                    triangles.AddRange(new[]{n,n+1,n+2});
                }
            }
        }
        void BuildSoundPulse()
        {
            float r=radius*Mathf.Clamp01(age/duration*2.5f);
            for(int spoke=0;spoke<8;spoke++)
                for(int j=0;j<48;j++)
                {
                    float u0=j/48f,u1=(j+1)/48f;
                    float a=spoke*Mathf.PI*.25f;
                    var dir=new Vector3(Mathf.Sin(a),0,Mathf.Cos(a));
                    var side=new Vector3(dir.z,0,-dir.x)*.045f;
                    float y0=.5f+Mathf.Sin(u0*18-age*12)*.4f*(1-u0);
                    float y1=.5f+Mathf.Sin(u1*18-age*12)*.4f*(1-u1);
                    Strip(dir*(r*u0)+Vector3.up*y0-side,dir*(r*u0)+Vector3.up*y0+side,
                          dir*(r*u1)+Vector3.up*y1-side,dir*(r*u1)+Vector3.up*y1+side,u0,u1,Mathf.Sin(u0*Mathf.PI));
                }
        }
        public static void Lightning(Vector3 a,Vector3 b,Color color,float dur=.22f)
        {
            var fx=Spawn(Shape.Lightning,a,Vector3.zero,1,dur,color);
            var delta=b-a; var cam=Camera.main;
            var side=Vector3.Cross(delta.normalized,cam?cam.transform.forward:Vector3.forward).normalized;
            if(side.sqrMagnitude<.01f) side=Vector3.right;
            var points=new Vector3[15];
            for(int i=0;i<15;i++) points[i]=delta*(i/14f)+(i==0 || i==14 ? Vector3.zero:side*Random.Range(-.22f,.22f));
            for(int i=0;i<14;i++)
                fx.Strip(points[i]-side*.04f,points[i]+side*.04f,points[i+1]-side*.04f,points[i+1]+side*.04f,i/14f,(i+1)/14f);
            for(int branch=0;branch<2;branch++)
            {
                var start=points[5+branch*4]; var end=start+delta.normalized*.4f+side*(branch==0?.5f:-.5f);
                fx.Strip(start-side*.025f,start+side*.025f,end-side*.008f,end+side*.008f,0,1,.55f);
            }
            fx.Upload();
        }
        void LateUpdate()
        {
            age+=Time.deltaTime;
            if(age>=duration || (shape==Shape.Shield && (!follow || follow.dead))) { Destroy(gameObject); return; }
            if(follow) transform.position=follow.Center;
            float fade=1-Mathf.SmoothStep(0,1,(age/duration-.78f)/.22f);
            material.SetFloat("_Fade",fade*Mathf.SmoothStep(0,1,age/.06f));
            if(shape==Shape.Sound || shape==Shape.Vortex || shape==Shape.Sweep || shape==Shape.SoundPulse) { Build(); Upload(); }
        }
        void OnDestroy() { if(mesh) Destroy(mesh); if(material) Destroy(material); }
    }
}
