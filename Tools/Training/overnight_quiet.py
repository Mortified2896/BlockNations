"""Time-limited macOS output mute and keyboard darkness for an authorized night.

No player, sound, display-wake assertion or training control is created. Native
readback is required after each setting change; audio routing may change anytime.
"""
import argparse
import ctypes as c
import json
import math
from pathlib import Path
import sys
import time

from supervisor import ROOT_MARKER, atomic_json


def four(value):
    return int.from_bytes(value.encode('ascii'), 'big')


class Address(c.Structure):
    _fields_ = [('selector', c.c_uint32), ('scope', c.c_uint32), ('element', c.c_uint32)]


class AudioOutput:
    def __init__(self):
        if sys.platform != 'darwin': raise RuntimeError('This quiet-host guard is for macOS.')
        self.lib = c.CDLL('/System/Library/Frameworks/CoreAudio.framework/CoreAudio')
        self.lib.AudioObjectGetPropertyData.argtypes = [c.c_uint32, c.POINTER(Address), c.c_uint32, c.c_void_p, c.POINTER(c.c_uint32), c.c_void_p]
        self.lib.AudioObjectGetPropertyData.restype = c.c_int32
        self.lib.AudioObjectSetPropertyData.argtypes = [c.c_uint32, c.POINTER(Address), c.c_uint32, c.c_void_p, c.c_uint32, c.c_void_p]
        self.lib.AudioObjectSetPropertyData.restype = c.c_int32

    def get(self, device, selector, scope):
        address = Address(four(selector), four(scope), 0); value = c.c_uint32(); size = c.c_uint32(4)
        status = self.lib.AudioObjectGetPropertyData(device, c.byref(address), 0, None, c.byref(size), c.byref(value))
        if status or size.value != 4: raise RuntimeError('Audio output property is unavailable: '+str(status))
        return value.value

    def default(self):
        device = self.get(1, 'dOut', 'glob')
        if not device: raise RuntimeError('No default audio output is available.')
        return device

    def muted(self, device):
        value = self.get(device, 'mute', 'outp')
        if value not in (0, 1): raise ValueError('Invalid output mute readback.')
        return value == 1

    def mute(self, device):
        address = Address(four('mute'), four('outp'), 0); value = c.c_uint32(1)
        status = self.lib.AudioObjectSetPropertyData(device, c.byref(address), 0, None, 4, c.byref(value))
        if status: raise RuntimeError('Could not mute current output: '+str(status))

    def quiet(self):
        repaired = False
        for attempt in range(2):
            device = self.default()
            if not self.muted(device): self.mute(device); repaired = True
            if self.default() == device:
                if not self.muted(device): raise RuntimeError('Current output did not acknowledge mute.')
                return dict(deviceId=device, muted=True, repaired=repaired)
        raise RuntimeError('Audio output changed repeatedly; retry on the next bounded tick.')


class KeyboardBacklight:
    def __init__(self):
        self.objc = c.CDLL('/usr/lib/libobjc.A.dylib')
        c.CDLL('/System/Library/PrivateFrameworks/CoreBrightness.framework/CoreBrightness')
        self.objc.objc_getClass.argtypes = [c.c_char_p]; self.objc.objc_getClass.restype = c.c_void_p
        self.objc.sel_registerName.argtypes = [c.c_char_p]; self.objc.sel_registerName.restype = c.c_void_p
        self.address = c.cast(self.objc.objc_msgSend, c.c_void_p).value
        cls = self.objc.objc_getClass(b'KeyboardBrightnessClient')
        if not cls: raise RuntimeError('Keyboard backlight settings are unavailable.')
        self.client = self.send(self.send(cls, 'alloc'), 'init')

    def send(self, obj, name, result=c.c_void_p, *args):
        return c.CFUNCTYPE(result, c.c_void_p, c.c_void_p, *[t for t, v in args])(
            self.address)(obj, self.objc.sel_registerName(name.encode()), *[v for t, v in args])

    def quiet(self):
        for name in ('copyKeyboardBacklightIDs', 'isKeyboardBuiltIn:', 'brightnessForKeyboard:',
                     'backlightLevelForKeyboard:', 'idleDimTimeForKeyboard:',
                     'setBrightness:forKeyboard:', 'setIdleDimTime:forKeyboard:'):
            if not self.send(self.client, 'respondsToSelector:', c.c_bool,
                             (c.c_void_p, self.objc.sel_registerName(name.encode()))):
                raise RuntimeError('Keyboard API changed; settings were not guessed.')
        ids = self.send(self.client, 'copyKeyboardBacklightIDs'); count = self.send(ids, 'count', c.c_ulong)
        if count > 16: raise ValueError('Unexpected keyboard backlight inventory.')
        keyboards = []
        for i in range(count):
            number = self.send(ids, 'objectAtIndex:', c.c_void_p, (c.c_ulong, i))
            keyboard = self.send(number, 'unsignedLongLongValue', c.c_ulonglong)
            arg = (c.c_ulonglong, keyboard)
            if not self.send(self.client, 'isKeyboardBuiltIn:', c.c_bool, arg): continue
            if self.send(self.client, 'brightnessForKeyboard:', c.c_float, arg) != 0:
                if not self.send(self.client, 'setBrightness:forKeyboard:', c.c_bool, (c.c_float, 0), arg):
                    raise RuntimeError('Keyboard did not acknowledge zero brightness.')
            if self.send(self.client, 'idleDimTimeForKeyboard:', c.c_double, arg) != 30:
                if not self.send(self.client, 'setIdleDimTime:forKeyboard:', c.c_bool, (c.c_double, 30), arg):
                    raise RuntimeError('Keyboard did not acknowledge the idle timeout.')
            brightness = self.send(self.client, 'brightnessForKeyboard:', c.c_float, arg)
            actual = self.send(self.client, 'backlightLevelForKeyboard:', c.c_float, arg)
            idle = self.send(self.client, 'idleDimTimeForKeyboard:', c.c_double, arg)
            if brightness != 0 or actual != 0 or idle != 30:
                raise RuntimeError('Keyboard darkness/idle readback is not yet confirmed.')
            keyboards.append(dict(brightness=brightness, actualBacklight=actual, idleDimSeconds=idle))
        return keyboards


def tick(config_path, *, now=None, audio_factory=AudioOutput, keyboard_factory=KeyboardBacklight):
    config_path = Path(config_path); config = json.loads(config_path.read_text())
    root = Path(config['root']); expires = config['expiresUnix']; now = time.time() if now is None else now
    if (config.get('owner') != 'BlockNations.QuietHost.v1' or config_path.parent != root or
            root.resolve() != root or json.loads((root/ROOT_MARKER).read_text()).get('owner') != 'BlockNations.LocalTraining.v1' or
            type(expires) not in (int, float) or not math.isfinite(expires) or expires-now > 12*3600):
        raise ValueError('Use an owned, time-limited overnight configuration.')
    path = config_path.with_name('overnight-quiet-status.json')
    state = json.loads(path.read_text()) if path.exists() else {}
    state.update(owner='BlockNations.QuietHost.v1', updatedUnix=now, expiresUnix=expires)
    if config.get('enabled') is not True or now >= expires:
        state['state'] = 'expired' if now >= expires else 'disabled'
        atomic_json(path, state); return state
    state.pop('audio', None); state.pop('keyboards', None)
    errors = []
    try:
        state['audio'] = audio_factory().quiet()
        state['audioRepairs'] = state.get('audioRepairs', 0)+int(state['audio']['repaired'])
    except (OSError, RuntimeError, ValueError) as error: errors.append(str(error))
    try: state['keyboards'] = keyboard_factory().quiet()
    except (OSError, RuntimeError, ValueError) as error: errors.append(str(error))
    state.update(state='fault' if errors else 'quiet', errors=errors)
    atomic_json(path, state); return state


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('--config', type=Path, required=True)
    args = parser.parse_args(); tick(args.config)
