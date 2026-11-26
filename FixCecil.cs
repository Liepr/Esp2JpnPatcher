// Reescribe llamadas de Mono.Cecil.dll (build de Mono) que no existen en .NET Framework 4.x.
using System; using System.Linq; using Mono.Cecil; using Mono.Cecil.Cil;
class Fix { static void Main(string[] a){
  var m = ModuleDefinition.ReadModule(a[0]);
  var arrT = new TypeReference("System","Array",m,m.TypeSystem.CoreLibrary);
  var rev1 = new MethodReference("Reverse", m.TypeSystem.Void, arrT){ HasThis=false };
  rev1.Parameters.Add(new ParameterDefinition(arrT));
  var rev3 = new MethodReference("Reverse", m.TypeSystem.Void, arrT){ HasThis=false };
  rev3.Parameters.Add(new ParameterDefinition(arrT)); rev3.Parameters.Add(new ParameterDefinition(m.TypeSystem.Int32)); rev3.Parameters.Add(new ParameterDefinition(m.TypeSystem.Int32));
  int n=0;
  foreach (var t in m.GetTypes()) foreach (var me in t.Methods.Where(x=>x.HasBody)) {
    var il = me.Body.GetILProcessor();
    foreach (var ins in me.Body.Instructions.ToList()) {
      var mr = ins.Operand as MethodReference; if (mr==null || ins.OpCode!=OpCodes.Call) continue;
      if (mr.DeclaringType.FullName!="System.Array") continue;
      var gi = mr as GenericInstanceMethod;
      if (mr.Name=="Reverse" && gi!=null) { ins.Operand = mr.Parameters.Count==1 ? rev1 : rev3; n++; }
      else if (mr.Name=="Empty" && gi!=null) {
        var elem = gi.GenericArguments[0];
        ins.OpCode = OpCodes.Ldc_I4_0; ins.Operand = null;
        il.InsertAfter(ins, il.Create(OpCodes.Newarr, elem)); n++;
      }
    }
  }
  // quitar nombre seguro (la firma original queda invalidada al modificar)
  m.Assembly.Name.PublicKey = new byte[0]; m.Assembly.Name.HasPublicKey = false;
  m.Attributes &= ~ModuleAttributes.StrongNameSigned;
  Console.WriteLine("reescritas: "+n);
  m.Write(a[1]);
}}
