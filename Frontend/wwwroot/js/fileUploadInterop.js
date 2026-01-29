window.fileUploadInterop = {
    triggerInput: function (inputRef, dotNetRef) {
        if (!inputRef) return;

        // Trigger folder input selection
        inputRef.click();

        inputRef.onchange = async function (event) {
            const files = Array.from(event.target.files);
            const fileData = [];

            for (const file of files) {
                const path = file.webkitRelativePath || file.name;

                const content = await new Promise(resolve => {
                    const reader = new FileReader();
                    reader.onload = () => resolve(reader.result);
                    reader.onerror = () => {
                        console.warn(`⚠️ Failed to read ${path}`);
                        resolve(""); // Skip unreadable files
                    };
                    reader.readAsText(file);
                });

                fileData.push({ path, content });
            }

            if (dotNetRef && typeof dotNetRef.invokeMethodAsync === "function") {
                try {
                    await dotNetRef.invokeMethodAsync("ReceiveFilesFromJS", fileData);
                } catch (err) {
                    console.error("🚫 Failed to call Blazor method:", err);
                }
            }
        };
    }
};
