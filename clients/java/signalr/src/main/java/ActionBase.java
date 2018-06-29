// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

import com.google.gson.Gson;
import com.google.gson.JsonArray;

import java.util.ArrayList;

public class ActionBase {
    private Action action;
    private Action1 action1;
    private Action2 action2;
    private Action3 action3;
    private Action4 action4;
    private Action5 action5;
    private Class type1;
    private Class type2;
    private Class type3;
    private Class type4;
    private Class type5;
    private Gson gson = new Gson();

    public ActionBase(Action action){
        this.action = action;
    }

    public <T1> ActionBase(Action1<T1> action, Class<T1> type1){
        this.action1 = action;
        this.type1 = type1;

    }

    public <T1, T2> ActionBase(Action2<T1, T2> action, Class<T1> type1, Class<T2> type2){
        this.action2 = action;
        this.type1 = type1;
        this.type2 = type2;
    }
    public <T1, T2, T3> ActionBase(Action3<T1,T2,T3> action, Class<T1> type1, Class<T2> type2, Class<T3> type3){
        this.action3 = action;
        this.type1 = type1;
        this.type2 = type2;
        this.type3 = type3;
    }

    public <T1, T2, T3, T4> ActionBase(Action4<T1,T2,T3,T4> action, Class<T1> type1, Class<T2> type2, Class<T3> type3, Class<T4> type4){
        this.action4 = action;
        this.type1 = type1;
        this.type2 = type2;
        this.type3 = type3;
        this.type4 = type4;
    }

    public <T1, T2, T3, T4, T5> ActionBase(Action5<T1, T2, T3, T4, T5> action, Class<T1> type1, Class<T2> type2, Class<T3> type3, Class<T4> type4, Class<T5> type5){
        this.action5 = action;
        this.type1 = type1;
        this.type2 = type2;
        this.type3 = type3;
        this.type4 = type4;
        this.type5 = type5;
    }

    public void invoke(Object[] args) throws Exception {
        if(this.action != null){
            this.action.invoke();
            return;
        }
        // At this point we know we have params sos we initialize an array to store the types to deserialize.
        ArrayList<Object> t = gson.fromJson((JsonArray)args[0], (new ArrayList<Object>()).getClass());
        if(action1 != null){
            action1.invoke(this.type1.cast(t.get(0)));
            return;
        }
        if(action2 !=null){
            action2.invoke(this.type1.cast(t.get(0)), this.type2.cast(t.get(1)));
            return;
        }
        if(action3 !=null){
            action3.invoke(this.type1.cast(t.get(0)), this.type2.cast(t.get(1)), this.type3.cast(t.get(2)));
            return;
        }
        if(action4 !=null){
            action4.invoke(this.type1.cast(t.get(0)), this.type2.cast(t.get(1)), this.type3.cast(t.get(2)),this.type4
                    .cast(t.get(3)));
            return;
        }
        if(action5 !=null){
            action5.invoke(this.type1.cast(t.get(0)), this.type2.cast(t.get(1)), this.type3.cast(t.get(2)),this.type4
                    .cast(t.get(3)), this.type5.cast(t.get(4)));
            return;
        }
    }
}
