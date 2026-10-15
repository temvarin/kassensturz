// Browser features the app calls from .NET (Services/Browser.cs).
window.geld = {
  // Browsers may delete a site's storage when the device runs low on space, unless the site is marked persistent.
  // They grant or refuse this on their own (installed apps usually get it) without asking the user.
  async protectStorage() {
    if (!navigator.storage?.persist) return false;
    return await navigator.storage.persisted() || await navigator.storage.persist();
  },

  // On a phone, the share sheet is how a file gets into Files, iCloud Drive or Google Drive; a computer downloads it.
  async saveFile(fileName, content) {
    const file = new File([content], fileName, { type: 'application/json' });
    if (matchMedia('(pointer: coarse)').matches && navigator.canShare?.({ files: [file] })) {
      try {
        await navigator.share({ files: [file] });
        return true;
      } catch (e) {
        if (e.name === 'AbortError') return false; // the user closed the share sheet
        // Sharing refused (e.g. not allowed in this context): fall back to a download.
      }
    }
    const link = Object.assign(document.createElement('a'), { href: URL.createObjectURL(file), download: fileName });
    link.click();
    setTimeout(() => URL.revokeObjectURL(link.href), 1000);
    return true;
  },
};
