﻿using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Game : MonoBehaviour
{
    enum ObjectType
    {
        Player,
        Rock,
        EnemyBullet,
        MyBullet
    }
    delegate GetSelf OnUpdate(GetSelf self);
    delegate Tuple<Vector3, Vector3, Quaternion, ObjectType, float, object> GetSelf();
    delegate Tuple<Vector3, Vector3, Quaternion, Color, Vector3[]> GetDraw();
    delegate GetDraw DrawSelf(GetSelf get);

    const float Acceleration = 0.06f;
    const float Damp = 7f;
    const float BulletSpeed = 0.6f;
    const float FadeSpeed = 2;

    static float widthScale;
    static Material material;
    static Material fadeMaterial;
    static Vector2 input;
    static RenderTexture currentRt;
    new static Camera camera;

    readonly List<GetDraw> drawList = new List<GetDraw>();
    readonly List<Tuple<GetSelf, OnUpdate, DrawSelf>> objectList = new List<Tuple<GetSelf, OnUpdate, DrawSelf>>();

    [RuntimeInitializeOnLoadMethod]
    public static void Initialize()
    {
        var go = new GameObject("Game");
        var game = go.AddComponent<Game>();
        var cams = FindObjectsOfType<Camera>();
        foreach (var cam in cams)
            Destroy(cam.gameObject);
        camera = go.AddComponent<Camera>();
        ReadyCamera(camera);

        material = new Material(Shader.Find("Sprites/Default"));
        fadeMaterial = new Material(material);
        fadeMaterial.color = Color.gray;
        game.StartCoroutine(game.StartGame());
    }

    IEnumerator StartGame()
    {
        objectList.Add(CreatePlayer());
        yield return StartCoroutine(UpdateGame());
    }

    static void ReadyCamera(Camera cam)
    {
        cam.clearFlags = CameraClearFlags.Nothing;
        currentRt = RenderTexture.GetTemporary(Screen.width, Screen.height);
        cam.targetTexture = currentRt;
        GL.Clear(true, true, Color.black);
    }

    IEnumerator UpdateGame()
    {
        while (true)
        {
            fadeMaterial.color = Color.white * (1 - Time.deltaTime * FadeSpeed);
            input.x = Input.GetAxis("Horizontal");
            input.y = Input.GetAxis("Vertical");
            widthScale = (float) Screen.height / Screen.width;
            drawList.Clear();
            for (var i = 0; i < objectList.Count; i++)
            {
                var obj = objectList[i];
                if (obj == null) continue;
                var nextSelf = obj.Item2(obj.Item1);
                if (nextSelf == null)
                {
                    objectList[i] = null;
                    continue;
                }
                drawList.Add(obj.Item3(nextSelf));
                objectList[i] = Tuple.Create(nextSelf, obj.Item2, obj.Item3);
            }
            yield return null;
        }
    }

    #region Player

    Tuple<GetSelf, OnUpdate, DrawSelf> CreatePlayer()
    {
        GetSelf getSelf = () =>
            Tuple.Create(
                Vector3.zero,
                Vector3.zero,
                Quaternion.identity,
                ObjectType.Player,
                1f / 80,
                (object) null);
        return Tuple.Create<GetSelf, OnUpdate, DrawSelf>(getSelf, UpdatePlayer, DrawPlayer);
    }

    GetSelf UpdatePlayer(GetSelf player)
    {
        var lastPlayer = player();
        var lastPosition = lastPlayer.Item1;
        var lastVelocity = lastPlayer.Item2;
        var lastRotation = lastPlayer.Item3;
        var type = lastPlayer.Item4;

        var velocity = lastVelocity * (1 - Time.deltaTime * Damp) + (Vector3) (Acceleration * input);
        var position = lastPosition + Time.deltaTime * velocity;
        if (position.x > 0.5f / widthScale) position.x -= 1 / widthScale;
        if (position.x < -0.5f / widthScale) position.x += 1 / widthScale;
        if (position.y > 0.5f) position.y -= 1;
        if (position.y < -0.5f) position.y += 1;
        var rotation = Quaternion.Lerp(lastRotation,
            Quaternion.Euler(0, 0, Mathf.Rad2Deg * Mathf.Atan2(-velocity.x, velocity.y)), 0.5f);

        if (Input.GetButtonDown("Jump"))
            objectList.Add(CreateMyBullet(position, rotation, velocity));

        var result = Tuple.Create(position, velocity, rotation, type, lastPlayer.Item5, (object) null);
        return () => result;
    }

    static GetDraw DrawPlayer(GetSelf getSelf)
    {
        var position = getSelf().Item1;
        var rotation = getSelf().Item3;
        var scale = (1f / 40) * Vector3.one;
        var color = Color.cyan;
        var verts = new[]
            {new Vector3(0, 0.6f), new Vector3(0.4f, -0.3f), new Vector3(0, -0.6f), new Vector3(-0.4f, -0.3f)};
        var tuple = Tuple.Create(position, scale, rotation, color, verts);
        return () => tuple;
    }

    #endregion

    #region MyBullet

    Tuple<GetSelf, OnUpdate, DrawSelf> CreateMyBullet(Vector3 position, Quaternion rotation, Vector3 velocity)
    {
        GetSelf getSelf = () =>
            Tuple.Create(
                position,
                rotation * Vector3.up * BulletSpeed + velocity,
                rotation,
                ObjectType.MyBullet,
                1f / 160,
                (object) 2f);
        return Tuple.Create<GetSelf, OnUpdate, DrawSelf>(getSelf, UpdateMyBullet, DrawMyBullet);
    }

    GetSelf UpdateMyBullet(GetSelf self)
    {
        var lastBullet = self();
        var lastPosition = lastBullet.Item1;
        var velocity = lastBullet.Item2;
        var rotation = lastBullet.Item3;
        var type = lastBullet.Item4;
        var lifetime = (float) lastBullet.Item5;
        lifetime -= lifetime - Time.deltaTime;

        var position = lastPosition + Time.deltaTime * velocity;
        var result = Tuple.Create(position, velocity, rotation, type, lastBullet.Item5, (object) lifetime);
        return () => result;
    }

    GetDraw DrawMyBullet(GetSelf getSelf)
    {
        var position = getSelf().Item1;
        var rotation = getSelf().Item3;
        var scale = (1f / 80) * Vector3.one;
        var color = Color.yellow;
        var verts = new[]
            {new Vector3(-0.2f, -0.7f), new Vector3(-0.2f, 0.7f), new Vector3(0.2f, 0.7f), new Vector3(0.2f, -0.7f)};
        var tuple = Tuple.Create(position, scale, rotation, color, verts);
        return () => tuple;
    }

    #endregion

    IEnumerable<GetSelf> TestCollision(GetSelf toTest)
    {
        foreach (var obj in objectList)
        {
            var one = toTest();
            var other = obj.Item1();
            var dist = Vector3.Distance(one.Item1, other.Item1);
            if (dist < one.Item5 + other.Item5) yield return obj.Item1;
        }
    }

    void DrawObject(GetDraw toDraw)
    {
        var target = toDraw();
        var position = target.Item1;
        var scale = target.Item2;
        var rotation = target.Item3;
        var color = target.Item4;
        var vertices = target.Item5;
        var trs = Matrix4x4.TRS(position, rotation, scale);
        GL.Color(color);
        foreach (var vert in vertices)
        {
            var multiplied = trs.MultiplyPoint(vert);
            multiplied.x *= widthScale;
            GL.Vertex(multiplied + 0.5f * Vector3.one);
        }
    }

    void OnPreRender() => camera.targetTexture = currentRt;

    void OnPostRender()
    {
        var newRt = RenderTexture.GetTemporary(Screen.width, Screen.height);
        var resizeRt = RenderTexture.GetTemporary(Screen.width >> 1, Screen.height >> 1);
        Graphics.Blit(currentRt, resizeRt);
        Graphics.Blit(resizeRt, newRt, fadeMaterial);
        camera.targetTexture = newRt;
        currentRt.Release();
        resizeRt.Release();
        currentRt = newRt;

        GL.PushMatrix();
        material.SetPass(0);
        GL.LoadOrtho();
        GL.Begin(GL.QUADS);
        drawList.ForEach(DrawObject);
        GL.End();
        GL.PopMatrix();
        camera.targetTexture = null;
        Graphics.Blit(currentRt, (RenderTexture) null);
    }
}