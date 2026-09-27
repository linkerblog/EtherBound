const BitCanvasGameSync = (() => {
  const databaseName = 'bitcanvas';
  const storeName = 'handles';
  const handleKey = 'sprites';
  let rememberedHandle = null;
  let handleLoaded = false;

  function openDatabase() {
    return new Promise((resolve, reject) => {
      const request = indexedDB.open(databaseName, 1);
      request.onupgradeneeded = () => request.result.createObjectStore(storeName);
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
  }

  async function storedHandle() {
    const database = await openDatabase();
    return new Promise((resolve, reject) => {
      const request = database.transaction(storeName).objectStore(storeName).get(handleKey);
      request.onsuccess = () => {
        database.close();
        resolve(request.result || null);
      };
      request.onerror = () => {
        database.close();
        reject(request.error);
      };
    });
  }

  async function storedFolderName() {
    try {
      return (await storedHandle())?.name || null;
    } catch {
      return null;
    }
  }

  async function saveHandle(handle) {
    try {
      const database = await openDatabase();
      await new Promise((resolve, reject) => {
        const transaction = database.transaction(storeName, 'readwrite');
        transaction.objectStore(storeName).put(handle, handleKey);
        transaction.oncomplete = resolve;
        transaction.onerror = () => reject(transaction.error);
      });
      database.close();
    } catch {
      // The active session can keep using its in-memory handle when persistence is unavailable.
    }
  }

  async function clearStoredHandle() {
    try {
      const database = await openDatabase();
      await new Promise((resolve, reject) => {
        const transaction = database.transaction(storeName, 'readwrite');
        transaction.objectStore(storeName).delete(handleKey);
        transaction.oncomplete = resolve;
        transaction.onerror = () => reject(transaction.error);
      });
      database.close();
    } catch {
      // A failed clear does not block choosing another folder for this session.
    }
  }

  async function validateFolder(handle) {
    if (handle.name !== 'sprites') return false;
    for (const name of ['grass', 'floor']) {
      try {
        await handle.getDirectoryHandle(name);
        return true;
      } catch (error) {
        if (error.name !== 'NotFoundError') throw error;
      }
    }
    return false;
  }

  async function requestFolder() {
    const handle = await window.showDirectoryPicker({ mode: 'readwrite', id: 'bitcanvas-sprites' });
    if (!await validateFolder(handle)) {
      return { error: "Pick EtherBound's src/sprites folder." };
    }
    rememberedHandle = handle;
    handleLoaded = true;
    await saveHandle(handle);
    return { handle };
  }

  async function chooseFolder() {
    const selection = requestFolder().catch((error) => ({
      error: error.name === 'AbortError' ? 'Folder selection cancelled.' : error.message,
    }));
    await clearStoredHandle();
    const result = await selection;
    rememberedHandle = result.handle || null;
    handleLoaded = true;
    return result;
  }

  async function getFolder() {
    let handle;
    if (!handleLoaded) return requestFolder();
    handle = rememberedHandle;
    if (!handle) return requestFolder();
    if (!await validateFolder(handle)) {
      await clearStoredHandle();
      rememberedHandle = null;
      handleLoaded = true;
      return { error: "Pick EtherBound's src/sprites folder." };
    }
    let permission = await handle.queryPermission({ mode: 'readwrite' });
    if (permission !== 'granted') permission = await handle.requestPermission({ mode: 'readwrite' });
    return permission === 'granted' ? { handle } : { error: 'Write permission was not granted.' };
  }

  function encode(canvas) {
    return new Promise((resolve, reject) => {
      canvas.toBlob((blob) => {
        if (blob) resolve(blob);
        else reject(new Error('PNG encoding failed.'));
      }, 'image/png');
    });
  }

  async function writeFile(directory, name, blob) {
    const file = await directory.getFileHandle(name, { create: true });
    const writable = await file.createWritable();
    await writable.write(blob);
    await writable.close();
  }

  async function send(folderName, canvases, names) {
    let copies;
    try {
      copies = canvases.map((source) => {
        const copy = document.createElement('canvas');
        copy.width = source.width;
        copy.height = source.height;
        copy.getContext('2d').drawImage(source, 0, 0);
        return copy;
      });
    } catch (error) {
      return { kind: 'fail', message: `Could not snapshot the sheets: ${error.message}` };
    }

    const folderPromise = getFolder().catch((error) => ({
      failure: error.name === 'AbortError' ? 'Folder selection cancelled.' : error.message,
    }));
    let result;
    let blobs;
    try {
      [result, blobs] = await Promise.all([
        folderPromise,
        Promise.all(copies.map((copy) => encode(copy))),
      ]);
    } catch (error) {
      return { kind: 'fail', message: error.message };
    }
    if (result.failure) return { kind: 'fail', message: result.failure };
    if (!result.handle) return { kind: 'warn', message: result.error };

    let directory;
    try {
      directory = await result.handle.getDirectoryHandle(folderName, { create: true });
    } catch (error) {
      return { kind: 'fail', message: `Could not open sprites/${folderName}/: ${error.message}` };
    }
    for (let index = 0; index < names.length; index += 1) {
      try {
        await writeFile(directory, names[index], blobs[index]);
      } catch (error) {
        return { kind: 'fail', message: `Could not write ${names[index]}: ${error.message}` };
      }
    }
    return { kind: 'act', message: `Sent ${names.join(', ')} to sprites/${folderName}/.` };
  }

  return {
    supported: typeof window.showDirectoryPicker === 'function',
    chooseFolder,
    storedFolderName: async () => {
      const name = await storedFolderName();
      if (!handleLoaded) {
        rememberedHandle = name ? await storedHandle() : null;
        handleLoaded = true;
      }
      return name;
    },
    send,
  };
})();
