using System;
using System.Collections.Generic;
using System.Text.Json;
namespace UnityEngine
{
 public struct Vector3
 {
  public float x,y,z;
  public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
  public static Vector3 zero=>new(0,0,0); public static Vector3 right=>new(1,0,0); public static Vector3 left=>new(-1,0,0); public static Vector3 forward=>new(0,0,1);
  public float sqrMagnitude=>x*x+y*y+z*z; public float magnitude=>MathF.Sqrt(sqrMagnitude);
  public Vector3 normalized=>magnitude>0?this*(1f/magnitude):zero;
  public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
  public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
  public static Vector3 operator *(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
  public static Vector3 operator /(Vector3 a,float b)=>a*(1/b);
 }
 public static class Mathf
 {
  public static float Max(float a,float b)=>MathF.Max(a,b); public static int Max(int a,int b)=>Math.Max(a,b);
  public static float Min(float a,float b)=>MathF.Min(a,b); public static int Min(int a,int b)=>Math.Min(a,b);
  public static int Clamp(int v,int a,int b)=>Math.Clamp(v,a,b); public static float Clamp(float v,float a,float b)=>Math.Clamp(v,a,b);
  public static float Clamp01(float v)=>Math.Clamp(v,0,1);
 }
 public static class JsonUtility
 {
  // Portable backend only: this cannot certify Unity JsonUtility behavior.
  public static T FromJson<T>(string json)=>JsonSerializer.Deserialize<T>(json,new JsonSerializerOptions{IncludeFields=true});
 }
}
namespace UnityEngine.Pool { public static class ListPool<T>{ public static List<T> Get()=>new(); public static void Release(List<T> value)=>value.Clear(); } }
