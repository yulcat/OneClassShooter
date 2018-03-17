using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

public class Game : MonoBehaviour
{
    [Flags]
    enum ObjectType
    {
        Player = 1,
        Rock = 2,
        EnemyBullet = 4,
        MyBullet = 8
    }
    delegate GetSelf OnUpdate(GetSelf self);
    delegate Tuple<Vector3, Vector3, Quaternion, ObjectType, float, object> GetSelf();
    delegate Tuple<Vector3, Vector3, Quaternion, Color, Vector3[]> GetDraw();
    delegate GetDraw DrawSelf(GetSelf get);

    const float Acceleration = 0.06f;
    const float Damp = 7f;
    const float BulletSpeed = 0.6f;
    const float FadeSpeed = 3;
    const float RockMinSpd = 0.1f;
    const float RockMaxSpd = 0.4f;
    const float RockRadius = 2f;
    const float RockSpawnRate = 5f;
    const float ShotRate = 10f;
    const float ShotSpread = 10f;

    static float widthScale;
    static Material material;
    static Material fadeMaterial;
    static Vector2 input;
    static RenderTexture currentRt;
    static Camera camera;

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
        var spawn = StartCoroutine(SpawnEnemies());
        while (objectList.Any(o => o.Item1().Item4 == ObjectType.Player))
        {
            fadeMaterial.color = Color.white * (Mathf.Pow(0.5f, Time.deltaTime * FadeSpeed));
            input.x = Input.GetAxis("Horizontal");
            input.y = Input.GetAxis("Vertical");
            widthScale = (float) Screen.height / Screen.width;
            drawList.Clear();
            for (var i = 0; i < objectList.Count; i++)
            {
                var obj = objectList[i];
                var nextSelf = obj.Item2(obj.Item1);
                if (nextSelf == null)
                {
                    objectList[i] = null;
                    continue;
                }
                drawList.Add(obj.Item3(nextSelf));
                objectList[i] = Tuple.Create(nextSelf, obj.Item2, obj.Item3);
            }
            objectList.RemoveAll(o => o == null);
            yield return null;
        }
        StopCoroutine(spawn);
    }

    IEnumerator SpawnEnemies()
    {
        while (true)
        {
            if (objectList.All(o => o.Item1().Item4 != ObjectType.Player)) yield return null;
            var player = objectList.First(o => o.Item1().Item4 == ObjectType.Player);
            var center = player.Item1().Item1;
            var direction = Random.insideUnitCircle.normalized;
            var rockPosition = center - (Vector3) direction * RockRadius;
            var speed = Random.Range(RockMinSpd, RockMaxSpd);
            objectList.Add(CreateRock(rockPosition,
                Quaternion.Euler(0, 0, Random.Range(0, 180)), speed * direction));
            Debug.Log($"Current Object Count : {objectList.Count}");
            yield return new WaitForSeconds(1 / RockSpawnRate);
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
                (object) 0f);
        return Tuple.Create<GetSelf, OnUpdate, DrawSelf>(getSelf, UpdatePlayer, DrawPlayer);
    }

    GetSelf UpdatePlayer(GetSelf player)
    {
        var lastPlayer = player();
        var lastPosition = lastPlayer.Item1;
        var lastVelocity = lastPlayer.Item2;
        var lastRotation = lastPlayer.Item3;
        var type = lastPlayer.Item4;
        var cooldown = (float) lastPlayer.Item6;

        var velocity = lastVelocity * (1 - Time.deltaTime * Damp) + (Vector3) (Acceleration * input);
        var position = lastPosition + Time.deltaTime * velocity;
        if (position.x > 0.5f / widthScale) position.x -= 1 / widthScale;
        if (position.x < -0.5f / widthScale) position.x += 1 / widthScale;
        if (position.y > 0.5f) position.y -= 1;
        if (position.y < -0.5f) position.y += 1;
        var rotation = Quaternion.Lerp(lastRotation,
            Quaternion.Euler(0, 0, Mathf.Rad2Deg * Mathf.Atan2(-velocity.x, velocity.y)), 0.5f);

        if (TestCollision(player, ObjectType.Rock).Any())
            return null;

        cooldown = Mathf.Max(0f, cooldown - Time.deltaTime);
        if (Input.GetButton("Jump") && cooldown <= 0)
        {
            objectList.Add(CreateMyBullet(position, rotation, velocity));
            cooldown += 1 / ShotRate;
        }

        var result = Tuple.Create(position, velocity, rotation, type, lastPlayer.Item5, (object) cooldown);
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
        var bulletDirection = rotation * Quaternion.Euler(0, 0, Random.Range(-ShotSpread, ShotSpread));
        GetSelf getSelf = () =>
            Tuple.Create(
                position,
                bulletDirection * Vector3.up * BulletSpeed + velocity,
                bulletDirection,
                ObjectType.MyBullet,
                1f / 160,
                (object) 1f);
        return Tuple.Create<GetSelf, OnUpdate, DrawSelf>(getSelf, UpdateMyBullet, DrawMyBullet);
    }

    GetSelf UpdateMyBullet(GetSelf self)
    {
        var lastBullet = self();
        var lastPosition = lastBullet.Item1;
        var velocity = lastBullet.Item2;
        var rotation = lastBullet.Item3;
        var type = lastBullet.Item4;
        var lifetime = (float) lastBullet.Item6;
        lifetime = lifetime - Time.deltaTime;
        if (lifetime < 0) return null;

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

    #region Rock

    Tuple<GetSelf, OnUpdate, DrawSelf> CreateRock(Vector3 position, Quaternion rotation, Vector3 velocity)
    {
        GetSelf getSelf = () =>
            Tuple.Create(
                position,
                velocity,
                rotation,
                ObjectType.Rock,
                1f / 160,
                (object) (RockRadius * 2 / velocity.magnitude));
        return Tuple.Create<GetSelf, OnUpdate, DrawSelf>(getSelf, UpdateRock, DrawRock);
    }

    GetSelf UpdateRock(GetSelf self)
    {
        var lastBullet = self();
        var lastPosition = lastBullet.Item1;
        var velocity = lastBullet.Item2;
        var rotation = lastBullet.Item3;
        var type = lastBullet.Item4;
        var lifetime = (float) lastBullet.Item6;
        lifetime = lifetime - Time.deltaTime;

        if (lifetime < 0) return null;
        if (TestCollision(self, ObjectType.MyBullet).Any())
            return null;

        var position = lastPosition + Time.deltaTime * velocity;
        var result = Tuple.Create(position, velocity, rotation, type, lastBullet.Item5, (object) lifetime);
        return () => result;
    }

    GetDraw DrawRock(GetSelf getSelf)
    {
        var position = getSelf().Item1;
        var rotation = getSelf().Item3;
        var scale = (1f / 30) * Vector3.one;
        var color = Color.red;
        var verts = new[]
        {
            new Vector3(0, -0.7f), new Vector3(-0.5f, -0.2f), new Vector3(-0.5f, 0.2f), new Vector3(0f, 0.7f),
            new Vector3(0, -0.7f), new Vector3(0.5f, -0.2f), new Vector3(0.5f, 0.2f), new Vector3(0f, 0.7f)
        };
        var tuple = Tuple.Create(position, scale, rotation, color, verts);
        return () => tuple;
    }

    #endregion

    #region EnemyBullet

    Tuple<GetSelf, OnUpdate, DrawSelf> CreateEnemyBullet(Vector3 position, Quaternion rotation, Vector3 velocity)
    {
        GetSelf getSelf = () =>
            Tuple.Create(
                position,
                velocity,
                rotation,
                ObjectType.EnemyBullet,
                1f / 160,
                (object) 2f);
        return Tuple.Create<GetSelf, OnUpdate, DrawSelf>(getSelf, UpdateEnemyBullet, DrawEnemyBullet);
    }

    GetSelf UpdateEnemyBullet(GetSelf self)
    {
        var lastBullet = self();
        var lastPosition = lastBullet.Item1;
        var velocity = lastBullet.Item2;
        var rotation = lastBullet.Item3;
        var type = lastBullet.Item4;
        var lifetime = lastBullet.Item5;
        lifetime = lifetime - Time.deltaTime;
        if (lifetime < 0) return null;

        var position = lastPosition + Time.deltaTime * velocity;
        var result = Tuple.Create(position, velocity, rotation, type, lastBullet.Item5, (object) lifetime);
        return () => result;
    }

    GetDraw DrawEnemyBullet(GetSelf getSelf)
    {
        var position = getSelf().Item1;
        var rotation = getSelf().Item3;
        var scale = (1f / 30) * Vector3.one;
        var color = Color.red;
        var verts = new[]
        {
            new Vector3(0, -0.7f), new Vector3(-0.5f, -0.2f), new Vector3(-0.5f, 0.2f), new Vector3(0f, 0.7f),
            new Vector3(0, -0.7f), new Vector3(0.5f, -0.2f), new Vector3(0.5f, 0.2f), new Vector3(0f, 0.7f)
        };
        var tuple = Tuple.Create(position, scale, rotation, color, verts);
        return () => tuple;
    }

    #endregion

    #region Particles

    // Item1 : Position
    // Item2 : Velocity
    // Item3 : Rotation
    // Item4 : Object Type
    // Item5 : Collision Radius
    // Item6 : {[Time, Lifetime], Color}
    Tuple<GetSelf, OnUpdate, DrawSelf> CreateParticle(Vector3 position, Quaternion rotation, Vector3 velocity,
        float lifetime, Color color)
    {
        var bulletDirection = rotation * Quaternion.Euler(0, 0, Random.Range(-ShotSpread, ShotSpread));
        GetSelf getSelf = () =>
            Tuple.Create(
                position,
                bulletDirection * Vector3.up * BulletSpeed + velocity,
                bulletDirection,
                ObjectType.MyBullet,
                1f / 160,
                (object) Tuple.Create(new Vector2(0, lifetime), color));
        return Tuple.Create<GetSelf, OnUpdate, DrawSelf>(getSelf, UpdateParticle, DrawParticle);
    }

    GetSelf UpdateParticle(GetSelf self)
    {
        var lastBullet = self();
        var lastPosition = lastBullet.Item1;
        var velocity = lastBullet.Item2;
        var rotation = lastBullet.Item3;
        var type = lastBullet.Item4;
        var item6 = (Tuple<Vector2, Color>) lastBullet.Item6;
        var lifetime = item6.Item1.x;
        lifetime = lifetime - Time.deltaTime;
        if (lifetime < 0) return null;

        var position = lastPosition + Time.deltaTime * velocity;
        var result = Tuple.Create(
            position,
            velocity,
            rotation,
            type,
            lastBullet.Item5,
            (object) Tuple.Create(new Vector2(lifetime, item6.Item1.y), item6.Item2));
        return () => result;
    }

    GetDraw DrawParticle(GetSelf getSelf)
    {
        var position = getSelf().Item1;
        var rotation = getSelf().Item3;
        var item6 = (Tuple<Vector2, Color>) getSelf().Item6;
        var scale = (1f / 80) * Vector3.one;
        var color = item6.Item2;
        var verts = new[]
            {new Vector3(-0.2f, -0.7f), new Vector3(-0.2f, 0.7f), new Vector3(0.2f, 0.7f), new Vector3(0.2f, -0.7f)};
        var tuple = Tuple.Create(position, scale, rotation, color, verts);
        return () => tuple;
    }

    #endregion

    IEnumerable<GetSelf> TestCollision(GetSelf toTest, ObjectType filter)
    {
        foreach (var obj in objectList.Where(o => o != null && (o.Item1().Item4 & filter) != 0))
        {
            if (obj.Item1 == null) continue;
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