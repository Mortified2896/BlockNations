using UnityEngine;

public class Unit : MonoBehaviour
{
    [Header("Owner")]
    public bool isPlayerOwned = true;

    [Header("Turn State")]
    public bool hasMovedThisTurn = false;

    [Header("City Link")]
    public City currentCity;

    [Header("Stats")]
    public int maxHealth = 2;
    public int currentHealth = 2;
    public int attack = 1;
    public int defense = 0;

    void Awake()
    {
        currentHealth = maxHealth;
    }
}
