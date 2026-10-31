// 浏览器自动填充/密码管理器直接写 DOM 值时不派发 input/change 事件，
// Blazor 侧模型收不到值；登录提交时由组件从这里兜底读回。
export function readLoginValues(emailId, passwordId) {
    const read = (id, name) => {
        const el = document.getElementById(id) || document.querySelector(`input[name="${name}"]`);
        return el && typeof el.value === "string" ? el.value : "";
    };
    return [read(emailId, "Email"), read(passwordId, "Password")];
}
