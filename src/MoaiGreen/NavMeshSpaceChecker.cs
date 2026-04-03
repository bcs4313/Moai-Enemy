using On;
using UnityEngine;
using UnityEngine.AI;

namespace MoaiEnemy.src.MoaiGreen
{
    public class NavMeshSpaceChecker : MonoBehaviour
    {
        public GameObject prefabToPlace; // The prefab to check space for
        public LayerMask navMeshLayer; // The layer mask for the NavMesh

        public static bool CanPlacePrefab(string target, Vector3 position, SpawnableOutsideObject obj = null)
        {
            // Get the size of the prefab
            Vector3 prefabSize = GetPrefabSize(target, obj);

            // Check if the position is on the NavMesh
            NavMeshHit hit;
            if (!NavMesh.SamplePosition(position, out hit, 1.0f, NavMesh.AllAreas))
            {
                Debug.LogWarning("Moai: - NavMeshSpaceChecker Position is not on NavMesh.");
                return false;
            }

            // Check if there's enough space for the prefab at the given position
            Vector3 halfExtents = prefabSize * 0.5f;
            Vector3 center = position + halfExtents;

            // Check if the area is clear using NavMesh.CalculatePath
            NavMeshPath path = new NavMeshPath();
            bool canPlace = NavMesh.CalculatePath(hit.position, center, NavMesh.AllAreas, path);

            if (!canPlace)
            {
                Debug.LogWarning("Not enough space to place prefab " + target + " at position: " + position);
                return false;
            }

            GameObject ship = GameObject.Find("HangarShip");
            if(ship)
            {
                // keep out of ship radius
                // (prevents turrets and other things from spawning inside of it)
                if(Vector3.Distance(ship.transform.position, position) < 30f)
                {
                    return false;
                }
            }
            else
            {
                Debug.LogWarning("Moai - NavMeshSpaceChecker - Can not find HangarShip");
            }

            return true;
        }

        private static Vector3 GetPrefabSize(string prefab, SpawnableOutsideObject obj = null)
        {
            // Calculate and return the size of the prefab
            // Example implementation:
            // Renderer renderer = prefabToPlace.GetComponent<Renderer>();
            // return renderer.bounds.size;

            // each vector goes in this format:
            // bound box size + player offset to go past the object (y doesnt matter for player)
            switch(prefab)
            {
                case "Turret":
                    return new Vector3(1f, 1.22f, 1f) + (new Vector3(0.73f, 0f, 0.36f) * 1.4f);
                case "Mine":
                    return new Vector3(1f, 1.22f, 1f) + (new Vector3(0.73f, 0f, 0.36f) * 1.4f);
                case "Circle":
                    return new Vector3(18f, 8f, 18f) + (new Vector3(0.73f, 0f, 0.36f) * 1.4f);
                case "MapObject":
                    return new Vector3(obj.objectWidth, obj.objectWidth, obj.objectWidth);
            }
            return Vector3.one; 
        }

        public static Vector3 GetRandomNavMeshPoint(float sampleRadius, Vector3 refPosition)
        {
            NavMeshHit hit;
            Vector3 randomPoint = refPosition;

            // Attempt to find a valid NavMesh point within sampleRadius distance
            Vector3 randomDirection = Random.insideUnitSphere * sampleRadius;
            randomDirection += refPosition; // Offset from this object's position

            if (NavMesh.SamplePosition(randomDirection, out hit, sampleRadius, NavMesh.AllAreas))
            {
                randomPoint = hit.position;
            }

            return randomPoint;
        }
    }
}
