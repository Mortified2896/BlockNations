using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [Header("Audio Sources")]
    public AudioSource sfxSource;
    public AudioSource uiSource;
    public AudioSource musicSource;
    public bool persistAcrossScenes = true;

    [Header("Music")]
    public AudioClip backgroundMusic;
    public AudioClip[] backgroundPlaylist;
    public bool loopPlaylist = true;
    public bool shufflePlaylist = false;
    public bool autoPlayMusic = true;
    [Range(0f, 1f)]
    public float musicVolume = 0.5f;

    [Header("UI Clips")]
    public AudioClip uiClickClip;
    public AudioClip uiHoverClip;
    public AudioClip invalidActionClip;
    public AudioClip selectUnitClip;

    [Header("Gameplay Clips")]
    public AudioClip moveClip;
    public AudioClip attackClip;
    public AudioClip[] attackClips;
    public AudioClip unitDownClip;
    public AudioClip[] unitDownClips;
    public AudioClip recruitClip;
    public AudioClip turnStartClip;
    public AudioClip turnEndClip;
    public AudioClip victoryClip;
    public AudioClip defeatClip;

    [Range(0f, 0.3f)]
    public float pitchJitter = 0.05f;

    [Header("Debug")]
    public bool debugLogInvalid = false;

    private Coroutine playlistRoutine;
    private bool loggedMissingInvalidClip;

    public bool HasPlaylistConfigured()
    {
        if (backgroundPlaylist == null || backgroundPlaylist.Length == 0)
            return false;

        foreach (AudioClip clip in backgroundPlaylist)
        {
            if (clip != null)
                return true;
        }

        return false;
    }

    public bool IsMusicPlaying()
    {
        return musicSource != null && musicSource.isPlaying;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (persistAcrossScenes)
        {
            DontDestroyOnLoad(gameObject);
        }

        EnsureSources();
    }

    void EnsureSources()
    {
        if (sfxSource == null)
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
        }

        if (uiSource == null)
        {
            uiSource = sfxSource;
        }

        if (musicSource == null)
        {
            musicSource = gameObject.AddComponent<AudioSource>();
        }

        if (musicSource != null)
        {
            musicSource.playOnAwake = false;
            musicSource.loop = true;
            musicSource.volume = musicVolume;
        }
    }

    AudioSource GetSource(AudioSource preferred)
    {
        if (preferred != null)
        {
            return preferred;
        }

        return sfxSource;
    }

    void PlayClip(AudioClip clip, AudioSource preferredSource, float volume = 1f)
    {
        if (clip == null)
            return;

        AudioSource source = GetSource(preferredSource);
        if (source == null)
            return;

        float originalPitch = source.pitch;
        if (pitchJitter > 0f)
        {
            source.pitch = UnityEngine.Random.Range(1f - pitchJitter, 1f + pitchJitter);
        }

        source.PlayOneShot(clip, Mathf.Clamp01(volume));
        source.pitch = originalPitch;
    }

    AudioClip PickClip(AudioClip[] options, AudioClip fallback)
    {
        if (options == null || options.Length == 0)
            return fallback;

        AudioClip chosen = null;
        int seen = 0;

        for (int i = 0; i < options.Length; i++)
        {
            AudioClip c = options[i];
            if (c == null) continue;

            seen++;
            if (UnityEngine.Random.Range(0, seen) == 0)
            {
                chosen = c;
            }
        }

        return chosen != null ? chosen : fallback;
    }

    public void PlayBackgroundMusic(AudioClip clip = null, bool restart = false)
    {
        StopPlaylist();

        AudioClip targetClip = clip != null ? clip : backgroundMusic;
        if (targetClip == null)
            return;

        AudioSource source = GetSource(musicSource);
        if (source == null)
            return;

        // Avoid restarting the same track unless requested.
        if (!restart && source.isPlaying && source.clip == targetClip)
            return;

        source.clip = targetClip;
        source.loop = true;
        source.volume = musicVolume;
        source.Play();
    }

    public void StopMusic()
    {
        StopPlaylist();

        AudioSource source = GetSource(musicSource);
        if (source != null)
        {
            source.Stop();
            source.clip = null;
        }
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
        {
            ResumeMusicIfNeeded();
        }
    }

    void OnApplicationPause(bool pauseStatus)
    {
        if (!pauseStatus)
        {
            ResumeMusicIfNeeded();
        }
    }

    void ResumeMusicIfNeeded()
    {
        AudioSource source = GetSource(musicSource);
        if (source == null)
            return;

        // Web/mobile browsers can suspend audio; when focus/pause returns,
        // try to resume if music is meant to be playing.
        if (autoPlayMusic && source.clip != null && !source.isPlaying)
        {
            source.Play();
        }
    }

    public void PlayPlaylist(bool restart = false)
    {
        if (backgroundPlaylist == null || backgroundPlaylist.Length == 0)
            return;

        if (!restart && playlistRoutine != null)
            return;

        StopPlaylist();

        AudioSource source = GetSource(musicSource);
        if (source == null)
            return;

        playlistRoutine = StartCoroutine(PlaylistRoutine(source));
    }

    void StopPlaylist()
    {
        if (playlistRoutine != null)
        {
            StopCoroutine(playlistRoutine);
            playlistRoutine = null;
        }
    }

    IEnumerator PlaylistRoutine(AudioSource source)
    {
        if (backgroundPlaylist == null || backgroundPlaylist.Length == 0)
            yield break;

        int index = 0;
        bool firstLoop = true;

        while (true)
        {
            AudioClip clip = backgroundPlaylist[index];
            if (clip != null)
            {
                source.clip = clip;
                source.loop = false;
                source.volume = musicVolume;
                source.Play();
                yield return new WaitWhile(() => source.isPlaying);
            }
            else
            {
                yield return null;
            }

            int nextIndex;
            if (shufflePlaylist && backgroundPlaylist.Length > 1)
            {
                do
                {
                    nextIndex = UnityEngine.Random.Range(0, backgroundPlaylist.Length);
                } while (nextIndex == index);
            }
            else
            {
                nextIndex = (index + 1) % backgroundPlaylist.Length;
            }

            if (!loopPlaylist && nextIndex == 0 && !firstLoop)
                break;

            index = nextIndex;
            firstLoop = false;
        }

        playlistRoutine = null;
    }

    void Start()
    {
        if (autoPlayMusic)
        {
            if (backgroundPlaylist != null && backgroundPlaylist.Length > 0)
            {
                PlayPlaylist();
            }
            else
            {
                PlayBackgroundMusic();
            }
        }
    }

    public void PlayUIClick()
    {
        PlayClip(uiClickClip, uiSource);
    }

    public void PlayUIHover()
    {
        PlayClip(uiHoverClip, uiSource, 0.8f);
    }

    public void PlayInvalid()
    {
        AudioClip clip = invalidActionClip != null ? invalidActionClip : uiClickClip;
        if (clip == null)
        {
            if (!loggedMissingInvalidClip)
            {
                loggedMissingInvalidClip = true;
                Debug.LogWarning("SoundManager: PlayInvalid called but no Invalid Action Clip (or UI Click Clip fallback) is assigned.", this);
            }
            return;
        }

        // Prefer UI source, but fall back to SFX source if UI is muted/disabled.
        AudioSource preferred = uiSource;
        if (preferred == null || !preferred.enabled || preferred.mute || preferred.volume <= 0f)
        {
            preferred = sfxSource;
        }

        if (debugLogInvalid)
        {
            string sourceLabel = preferred != null ? preferred.GetType().Name + "@" + preferred.gameObject.name : "<null>";
            float sourceVolume = preferred != null ? preferred.volume : 0f;
            bool sourceMuted = preferred != null && preferred.mute;
            Debug.Log($"SoundManager: PlayInvalid clip='{clip.name}', source={sourceLabel}, vol={sourceVolume}, mute={sourceMuted}", this);
        }

        PlayClip(clip, preferred);
    }

    [ContextMenu("Test/Play Invalid Action")]
    void TestPlayInvalidAction()
    {
        PlayInvalid();
    }

    public void PlayUnitSelect()
    {
        if (selectUnitClip != null)
        {
            PlayClip(selectUnitClip, uiSource);
        }
        else
        {
            PlayUIClick();
        }
    }

    public void PlayMove()
    {
        PlayClip(moveClip, sfxSource);
    }

    public void PlayAttack()
    {
        AudioClip clip = PickClip(attackClips, attackClip);
        PlayClip(clip, sfxSource);
    }

    public void PlayUnitDown()
    {
        AudioClip clip = PickClip(unitDownClips, unitDownClip);
        PlayClip(clip, sfxSource);
    }

    public void PlayRecruit()
    {
        PlayClip(recruitClip, sfxSource);
    }

    public void PlayTurnStart()
    {
        PlayClip(turnStartClip, sfxSource);
    }

    public void PlayTurnEnd()
    {
        PlayClip(turnEndClip, sfxSource);
    }

    public void PlayGameOver(bool playerWon)
    {
        if (playerWon)
        {
            if (victoryClip != null)
            {
                PlayClip(victoryClip, sfxSource);
                return;
            }
        }
        else
        {
            if (defeatClip != null)
            {
                PlayClip(defeatClip, sfxSource);
                return;
            }
        }

        PlayClip(victoryClip ?? defeatClip, sfxSource);
    }
}
