"""Read macOS thermal pressure without launching a game or changing settings."""
import ctypes
import sys


def thermal_state():
    """Foundation's live nominal/fair/serious/critical state (0 through 3)."""
    if sys.platform != 'darwin':
        return 0
    ctypes.CDLL('/System/Library/Frameworks/Foundation.framework/Foundation')
    objc = ctypes.CDLL('/usr/lib/libobjc.A.dylib')
    objc.objc_getClass.argtypes = [ctypes.c_char_p]
    objc.objc_getClass.restype = ctypes.c_void_p
    objc.sel_registerName.argtypes = [ctypes.c_char_p]
    objc.sel_registerName.restype = ctypes.c_void_p
    address = ctypes.cast(objc.objc_msgSend, ctypes.c_void_p).value
    object_call = ctypes.CFUNCTYPE(ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p)(address)
    state_call = ctypes.CFUNCTYPE(ctypes.c_long, ctypes.c_void_p, ctypes.c_void_p)(address)
    cls = objc.objc_getClass(b'NSProcessInfo')
    if not cls:
        raise RuntimeError('macOS thermal pressure is unavailable.')
    info = object_call(cls, objc.sel_registerName(b'processInfo'))
    state = state_call(info, objc.sel_registerName(b'thermalState'))
    if state not in (0, 1, 2, 3):
        raise ValueError('Invalid live macOS thermal pressure.')
    return state
